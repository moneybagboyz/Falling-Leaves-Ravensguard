# Phase 4: Dwarf Fortress calibration notes (resolver R1)

The resolver's physics follows the Dwarf Fortress wiki's reverse-engineered combat rules. Until November 2026 the wiki could not be fetched from the build environment, and these
notes recorded what a web search returned. The user then approved reading the wiki, and the Phase 4 close-out re-checked every number against the live pages (raw wikitext,
`index.php?title=<Page>&action=raw`, read only). This file now records what the pages say, how ours compares, and the decision for each difference.

Pages read: **DF2014:Material science** (Attack momentum, Attack momentum costs, Contact area, Elastic material modifiers, Penetration depth, Pulping, the materials table),
**DF2014:Weapon** (the weapon tables, Size, Material, Combat formulae, Combat testing), **DF2014:Combat**, **DF2014:Armor** (Layers, Coverage), **DF2014:Ammunition**,
**DF2014:Attack**, **DF2014:Wound**, **DF2014:Item quality** and its **/Table**, **DF2014:Edge**, **DF2014:Weight**, **Gold** (main namespace), the talk pages
**DF2014 Talk:Weapon** and **DF2014 Talk:Material science**, and the DF2014 raws as the wiki shows them (`inorganic_metal.txt`, `material_template_default.txt`, `body_default.txt`).

## What the wiki says

### Momentum (Material science, "Attack Momentum"; Weapon, "Combat formulae")
- The Weapon page: **M = Skill x Size x Str x Vel / (10^6 x (1 + i_Size / (w_density x w_size)))**. *Skill* is "a gradual multiplier based on skill level, from 1x base up to 2x
  at Grand Master"; *Size* the species' average size (dwarf 60,000); *i_Size* the individual's (dwarves 33,750 to 93,750); *Str* the strength (1,250 for an average dwarf); *Vel* the
  attack's velocity multiplier; *w_density* and *w_size* the weapon material's density and the weapon's volume in cm3. Quick attacks halve it, wild and heavy ones x1.5, a prone
  target x2. There is also a hard velocity limit of 10,000 that no unmodded weapon reaches.
- **The i_Size term is not the same in every form the wiki gives.** The Material science page writes three: (1) `M = Str x Vel / (10^6 / Size + 10 F / W)` with F = i_Size / Size
  and W the weapon's mass in kilograms (DF2014:Weight: Γ = density x volume / 10^6); (2) `M = Size x Str x Vel / (10 x (10^5 + i_Size / W))` (the dwarf line; the general line is
  garbled); (3) the Weapon page's form above. Forms (1) and (2) give an i_Size term of `10 x i_Size / (w_density x w_size)`; form (3) drops the 10.
  - Form (3) is the one the **battle-axe table** is computed from (a dwarf of 60,000 with a steel axe of size 800: 93.75 / (1 + 60,000 / 6,280,000) = 92.8628, the table's cell).
    The main-namespace Gold page agrees with it ("blunt attacks will have less than 1% more momentum than silver, despite having almost twice the density").
  - Form (1) is the one the Material science **materials table** uses (a dwarf's steel short sword 75, mace 137; adamantine 9 and 31), and its prose ("a platinum war hammer in dwarven
    hands only gets about 12% more momentum over a steel one ... an adamantine hammer only 1/7th as much"); the Weapon page's Combat testing calls adamantine "a terrible choice for
    blunt weapons". The Weapon talk page records an earlier slip in the same derivation (6 x 10^5 for a dwarf's 6 x 10^4, fixed in 2018).
- "Weapon weight matters very little past a certain threshold ... since all common weapon metals have about the same density, it can be safely ignored" (Material science).

### The weapon tables (Weapon, "Native weapons", "Foreign weapons", "Size")
Velocity multipliers: every slash and hack 1.25 (short sword, long sword, 2H sword, scimitar, battle axe, great axe, halberd, dagger); every stab 1.0 (swords, dagger, spear, pike,
halberd); flat slaps 1.25; pommel strikes 1.0; shaft bashes 1.25; mace 2.0; war hammer 2.0; maul 2.0; morningstar 2.0; flail 2.5; scourge 2.0; whip 5.0; a crossbow or bow in
the hand 1.25. Contact areas: slashes 20,000 to 100,000 (in effect the target layer's area), stabs 5 to 50, spear 20, mace 20, war hammer 10, maul 100, flail 200. Penetration: sword
slash 4,000 and stab 2,000, spear 10,000, pike 12,000, battle axe 6,000, dagger stab 1,000 (ignored for blunt attacks). Sizes (cm3): dagger 200, short sword 300, scimitar 300,
spear 400, war hammer 400, flail 500, long sword 700, battle axe 800, mace 800, pike 800, 2H sword 900, halberd 1,200, great axe 1,300, maul 1,300.

### The layer tests (Material science, "Attack Momentum Costs"; Weapon, "Combat formulae")
- **Edged:** a layer is cut when `M >= (rSY + (A + 1) x rSF) x (10 + 2 Qa) / (S x Qw)`, rSY and rSF the layer's shear yield and fracture over the weapon's, A the contact area;
  then "momentum is decreased by some 5%" and the next layer is tested. Otherwise the blow is "converted to blunt *just for this layer*".
- **Deflection:** a blunt blow bounces off armour when `2 x Sw x IYw < A x Da` (weapon size, weapon impact yield in MPa, area, armour density in g/cm3). "Most dedicated blunt
  weapons cannot be deflected by anything but slade."
- **Blunt:** otherwise the layer is crushed when `M >= (2 IF - IY) x (2 + 0.4 Qa) x A` (the layer's impact fracture and yield in MPa); again about 5% is lost, "if the attack
  was edged, it becomes edged again".
- **Both fail:** the blow becomes blunt for good and keeps `M x SHEAR_STRAIN_AT_YIELD / 50,000` (edged) or `IMPACT_STRAIN_AT_YIELD / 50,000` (blunt): "most metals reduce blocked
  attacks by 98%-99%". Chain (`STRUCTURAL_ELASTICITY_CHAIN_*`: mail shirts, leggings) has both strains raised to 50,000, x1; woven cloth's shear falls to "negligible 20-30 kPa".
- **Shear yield:** "doesn't actually matter" (Material science); "Shear Fracture is significantly more important than Shear Yield" (Weapon). Both keep it in the formula.
- **Contact area** is the minimum of the weapon's and the layer's; a body part's size is the creature's size times the part's relative size (human: upper and lower body 18%, neck
  1.8%, head 5.4%, upper and lower arm 3.6%, hand 1.4%, upper leg 9.0%, lower leg 7.2%, foot 2.2%; the size columns, human 70,000, give the hand 1,009 = 1.44% and the foot
  1,514 = 2.16%, which are the raws' relsizes 80 and 120 against the trunk's 1,000).
- **Sharpness S** (Edge, Material science): the material's MAX_EDGE over 10,000: 1 for most metals, 1.2 divine metal, 1.5 glass, 2 obsidian, 10 adamantine, 0.1 everything else
  (stone, bone and wood are 1,000).
- **Quality** (Item quality/Table): sharpness 50, 60, 70, 80, 90, 100% of the max edge from standard to masterwork, and 100% for an artifact ("artifact edges are the maximum edge
  for the material, which is also what a masterwork gets"; a long-standing bug gives artifacts half); to-hit and armour deflect 1, 1.2, 1.4, 1.6, 1.8, 2, artifact 3. So
  **Qw** = 1, 1.4 fine, 2 masterwork "(or artifact)", and **Qa** "same but x3 for artifacts". "Armor quality doesn't matter much: masterwork armor provides only about 15% more
  protection." The Material science "min M" column (the momentum a mace of area 20 needs to break each armour) is `(2 IF - IY) x 2 x 20`, i.e. computed with Qa 0.
- **Penetration depth** is how far an edge shears before it turns blunt; "if the penetration depth is greater than the size of the struck body part, the body part may be sliced off".
- **Pulping:** a part is pulped when every layer is 100% bruised, 250% dented or 100% cut.
- **Layers** (Armor), inner to outer: under, over, armour, cover. **Coverage** is the chance a blow on a covered part meets the piece; pieces reach further parts by steps
  (a mail shirt UBSTEP 1 and LBSTEP 1: upper arms, neck, upper legs; a breastplate the trunk only).
- **Ranged:** a launcher fires with momentum `SHOOT_FORCE / 20` capped at velocity `SHOOT_MAXVEL / 10` (bows and crossbows 1,000 and 200: a vanilla bolt carries 50); all
  ammunition is EDGE, size 150, area 5, penetration 1,000.
- **Rankings** (Weapon, Combat testing): edged adamantine > steel > iron, "matched by the bronzes" > copper > silver; blunt platinum, slade > steel, silver > copper, bronzes, iron
  > adamantine ("all six standard weapon metals perform nearly identically").
- **Materials** (the DF2014 raws): steel 7,850 / IY 1,505,000 / IF 2,520,000 / IS 940 / SY 430,000 / SF 720,000 / SS 215; iron 7,850 / 542,500 / 1,085,000 / 319 / 155,000 /
  310,000 / 189; bronze 8,250 / 602,000 / 843,500 / 547 / 172,000 / 241,000 / 156; copper 8,930 / 245,000 / 770,000 / 175 / 70,000 / 220,000 / 145; silver 10,490 / 350,000 /
  595,000 / 350 / 100,000 / 170,000 / 333; gold 19,320 / 175,000 / 350,000 / 97 / 50,000 / 100,000 / 185; lead 11,340 / 35,000 / 42,000 / 760 / 10,000 / 12,000 / 179; tin
  7,280 / 42,000 / 350,000 / 724 / 12,000 / 100,000 / 66; every metal MAX_EDGE 10,000. Templates: bone 500 / 200,000 / 200,000 / 100 / 115,000 / 130,000 / 100, edge 1,000;
  leather 500 / 10,000 / 10,000 / 50,000 / 25,000 / 25,000 / 50,000; wood 500 / 10,000 / 10,000 / 1,000 / 40,000 / 40,000 / 1,000, edge 1,000; skin 1,000 / 10,000 / 10,000 /
  50,000 / 20,000 / 20,000 / 50,000; fat 900 / 10,000 / 10,000 / 50,000 / 10,000 / 10,000 / 50,000; muscle 1,060 / 10,000 / 10,000 / 50,000 / 20,000 / 20,000 / 50,000;
  nerve 500 / 10,000 / 10,000 / 50,000 / 20,000 / 20,000 / 50,000; cartilage 500 / 10,000 / 10,000 / 25,000 / 30,000 / 30,000 / 25,000; hair 500 / 10,000 / 10,000 / 100,000 /
  60,000 / 120,000 / 100,000. The Weapon page's metal table is the v0.31 raws rounded to thousands (iron 542 / 1,080, bronze 843).

## Comparison with ours
Ours: `Combat/Resolver2/LayerTest.cs`, `ResolverV2.cs`, `CombatTablesV2.cs`; content `materials/*.jsonc`, `items/weapons.jsonc`, `combat/ranged.jsonc`, `bodies/human.jsonc`.

| # | Item | Wiki | Ours | Match? | Decision |
|---|---|---|---|---|---|
| 1 | Momentum formula | form (3) above | `BaseMomentumMilli`: `Size x Str / 1000 x Vel_pm x mass / (mass + i_Size) / 1000` milli, mass = kg/m3 x cm3 (mg) | match | RS-1b: every cell of the battle-axe table within 1 milli |
| 2 | The i_Size term | 1 x (form 3, the table, Gold page) or 10 x (forms 1 and 2, the materials table, the platinum and adamantine prose) | 1 x | the wiki disagrees with itself | **Keep 1 x** (below) |
| 3 | Size and i_Size | species average, individual | `SpeciesSizeCm3`, `SizeCm3` (human 70,000) | match | - |
| 4 | Skill | 1x to 2x at Grand Master | `1 + skill / 100`, skill 0 to 100 | match | - |
| 5 | Quick, heavy, prone | x0.5, x1.5, x2 | the same | match | - |
| 6 | Speed limit | velocity 10,000 | momentum clamp 2,000 units | differs | Keep: our units; no blow reaches either |
| 7 | Velocity multipliers | slash 1.25, stab 1.0, mace and hammer 2.0, maul 2.0, flail 2.5, shaft and flat 1.25, pommel 1.0 | slashes 1.25 (sword, longsword, axe, hand axe), falchion 1.3, great axe 1.2, dagger 1.3 / 1.2; points 1.25 to 1.3; mace 1.5, war hammer 1.1, maul 1.7, flail 1.2; flats 0.7, pommels and butts 0.8 | differs | **Keep** (design): never transcribed; they are "DF-style estimates, calibrated with tools/calibrate against resolver-v2 section 8" (weapons.jsonc) with our real areas, real masses and the C1 to C3 rules. P4-C raised the mace from 1.0 to 1.5, short of DF's 2.0, when it already won 69% against a padded swordsman; the maul's 1.7 is close-out item 2 (at 0.9 it could not crush a breastplate) |
| 8 | Contact areas | DF units: slashes 20,000 to 60,000 (the layer's area wins), stabs 5 to 50, mace 20, hammer 10, maul 100 | real cm2: edges 12 to 40, points 3 to 15, mace 20, hammer 10, maul 25 | differs | Keep (design: SI units). The min(weapon, layer) rule is the same |
| 9 | Penetration | DF units; a sword's slash 4,000 reaches further than its stab 2,000 | mm of reach: sword slash 70, stab 450 (the blade's length), spear 400, axe 110 | differs | Keep (design: real geometry; severing is rule C4) |
| 10 | Weapon size | DF volumes (short sword 300, battle axe 800, mace 800, maul 1,300) | real volumes (arming sword 180 + 60 ash, axe 120 + 600 ash, mace 250 + 300, maul 600 + 800) | differs | Keep (design: real weapon masses, 1.45 kg sword) |
| 11 | Weapon mass | density x volume | the same, head plus haft | match | - |
| 12 | Edged need | the formula | `CutNeedMilli`, ppm ratios, x `cut_resist_pm` on tissues | match | RS-3, RS-19 |
| 13 | rSY | "doesn't matter" / minor | kept in the formula | match | Keep (both pages keep it; it is a few % of the need) |
| 14 | Sharpness S | metals 1, everything else 0.1; obsidian 2, adamantine 10 | `max_edge` 1.0 metals, 0.1 wood and bone | match | No glass or obsidian blade in content yet |
| 15 | Qw | 1, 1.2, 1.4, 1.6, 1.8, 2, artifact 2 | artifact was 3 | **mistake** | **Fixed**: `CombatTablesV2.EdgeQualityPm`, `QualityTable.EdgeMultiplier` (RS-26). No artifact weapon exists in content: no fight changes |
| 16 | Qa | 1, 1.2, 1.4, 1.6, 1.8, 2, artifact 3 | `CombatTablesV2.QualityPm`; clothing books also Crude 0.8, Poor 0.9 | match (and more) | Keep: our goods have grades below standard, DF's do not (RS-26: masterwork armour +16.7%, the wiki's "about 15%") |
| 17 | Cut success | about -5% | `min(0.95 M, M - 0.6 need)` | differs | Keep (design: rule C1, the spike's lethality finding) |
| 18 | An edge failing a layer | blunt for that layer only; edged again after the layer is crushed | blunt for good | differs | **Keep** (design): with real cm2 areas iron mail's crush need (117 at a sword's 30 cm2) is within a sword's reach, so DF's rule would let every sword that crushes mail cut the flesh beneath it; resolver-v2 8 wants "mail turns cuts into blunt blows" (RS-4, RS-10) |
| 19 | Deflection | `2 Sw IYw < A Da`, armour layers | the same, rigid shells only | differs | Keep (close-out item 2: a club bounced off a linen shirt). RS-11, RS-30 (slade alone turns a DF war hammer) |
| 20 | Blunt need | `(2 IF - IY)(2 + 0.4 Qa) A` | `BluntNeedMilli`, the same (x `crush_resist_pm` on tissues) | match | RS-27 reproduces the "min M" column at Qa 0; ours uses Qa 1 for a standard piece, the formula's legend (x 1.2 the column) |
| 21 | Blunt success | about -5% | elastic: 0.95 M; else `min(0.95 M, M - 0.6 need)`, then padding absorbs | differs | Keep (design: rules C1 and C3) |
| 22 | Both fail | `M x strain / 50,000`, mail x1 | `AfterFail`, the same (capped at x1); a failed edge on a rigid shell skids by it without the crush test | match (plus a rule) | Keep (close-out item 2: a dagger's point crushed a breastplate like a hammer). RS-9, RS-30 |
| 23 | Woven cloth | shear 20 to 30 kPa | `min(SY, 20,000)`, `min(SF, 30,000)` | match | - |
| 24 | Contact area | min(weapon, layer); armour size = part size x coverage x size multiplier x steps | `min(A, part size x coverage)` | match (rule) | Keep: our areas are always below a layer's, so the armour-size terms never bind |
| 25 | Part sizes | hand 1.44%, foot 2.16% (relsizes 80 and 120; the percent column rounds them to 1.4 and 2.2) | hand 0.014, foot 0.022 | **transcription** | **Deferred** to the abstract re-fit: at 0.0144 and 0.0216 the live fights barely move, but the rebuilt wound tables put AB-3's scenario A casualties (abstract 3.85 against the live 3.45) past the test's 10% band (9.2% before this work, 9.9% with the material fixes alone); the fit lives in Sim/Combat/Abstract. RS-29 allows the two parts 3% until then |
| 26 | Human size | 70,000 | 70,000 | match | RS-29 |
| 27 | Layer order | under, over, armour, cover | the same | match | - |
| 28 | Coverage | chance to meet the piece; whole parts by steps | a fraction per part in content (a hauberk 0.6 of the upper leg) | differs | Keep (design: a skirt or a sleeve covers part of a limb) |
| 29 | Pulping | every layer 100% bruised, 250% dented or 100% cut | not modelled | differs | Keep: the wound grades (mangled muscle, shattered bone) and the wound book stand for it |
| 30 | Slicing off | penetration greater than the part | rule C4: still edged after the bone, reach left after armour >= the part's depth, 30% of M0 left | differs | Keep (design: severing is rare, resolver-v2 8) |
| 31 | Ranged momentum | `SHOOT_FORCE / 20`, velocity cap `SHOOT_MAXVEL / 10`; a bolt 50 | SI: `mass_g x v x power x 485 / 10,000` with drag (bodkin 120.0, quarrel 132.4) | differs | Keep (design: SI) |
| 32 | Ammunition | all EDGE, size 150, area 5, penetration 1,000 | bodkin point 3 / 250 mm, broadhead edge 12 / 120 mm, quarrel point 3 / 200 mm, a blunt arrow | differs | Keep (design: real heads; the blunt arrow is historical, DF has none) |
| 33 | Steel, copper, silver | the raws | the raws | match | RS-28 |
| 34 | Iron, bronze impact | iron IY 542,500, IF 1,085,000; bronze IF 843,500 | 542,000, 1,080,000; 843,000 (the Weapon page's v0.31 table, rounded) | **transcription** | **Fixed** to the DF2014 raws (RS-28) |
| 35 | Gold, lead, tin | the raws | impact yields and strains were estimates (gold IY 250,000 IS 600; lead 30,000 / 2,348; tin 150,000 / 1,862) | differs | **Fixed** to the raws (not weapon or armour metals in any kit) |
| 36 | Leather | SY 25,000, SF 25,000, strains 50,000 | SY 20,000, SF 30,000, strains 100,000 (the materials table's rounded "0.03" and "100%") | **transcription** | **Fixed** to the raws (the strains change nothing: x1 either way) |
| 37 | Bone (the material) | SY 115,000, strains 100 | 100,000, strains 1,000 (estimates) | differs | **Fixed** to the raws |
| 38 | Wood | density 500, SY 40,000, strains 1,000 | oak 750 (ash 680), SY 20,000, strains 2,000 (estimates) | differs | **Fixed** SY and strains; density kept (design: real oak and ash) |
| 39 | Body tissues | the templates above (skin, fat, muscle, nerve, cartilage, hair, bone) | tuned: skin IF 20,000 SY 10,000; fat 5,000 / 8,000; muscle IF 15,000, SY 8,000, SF 12,000; nerve, hair, cartilage their own; bone SY 100,000, strain 1,000, cut x2.5, crush x5; real densities | differs | **Keep** (design): tuned with the C1 to C3 rules and the bone multipliers against resolver-v2 8; flesh needs are a few units against blows of 50 to 300, so the keep-95% rule decides what passes, and bone's stopping power is rule C2's, not its raw numbers |
| 40 | Cloth (linen, wool, silk) | not checked (DF's cloth comes from plant, hair and silk templates) | estimates; the woven rule sets their shear | - | Keep |
| 41 | Edged ranking | adamantine > steel > iron >= bronzes > copper > silver | silver < copper < bronze < iron < steel | match | The `MetalRanking` golden |
| 42 | Blunt metals | "nearly identically"; weight matters little | our mace in five metals within 1%; a DF gold hammer < 1% over silver | match | RS-25 |

**Why the i_Size term stays at 1 x (row 2).** The wiki gives both, and the worked example the goldens transcribe (the battle-axe table) is the 1 x form; RS-1b reproduces it.
The 10 x form was measured (2,000 duels per pairing, the P4-C table): the plated duels lengthen from 23.1 to 35.0 s, the mace against a plated swordsman falls from 62% to 38%
(the target is that it wins), the mace against a padded one from 70% to 35%, and the spear against a padded swordsman from 59% to 52%: every melee number would need recalibrating.
It would also bite our weapons far harder than DF's: ours are real volumes (an arming sword 1.45 kg, a dagger 0.33 kg) and would lose 30 to 70% of their swing to a 70,000 human,
where DF's own (a steel short sword 2.36 kg, a battle axe 6.28 kg) lose 9 to 20% to a 60,000 dwarf. A weapon's weight is carried by its velocity multiplier instead (the maul's 1.7).

## Goldens (the wiki's numbers)
- **RS-1b** the battle-axe momentum table, 28 dwarf sizes x 8 metals, within 1 milli (our milli floors, the wiki rounds to 4 places).
- **RS-25** weight matters little: a DF gold war hammer (size 400, velocity 2) has under 1% more momentum than a silver one (the Gold page); our mace in iron, steel, bronze, copper
  and silver within 1% (Combat testing).
- **RS-26** the Item quality table: Qa 1 to 2 and 3 for an artifact, Qw the same but 2 for an artifact; masterwork armour +16.7% (the wiki's "about 15%"); a masterwork blade halves
  the need.
- **RS-27** the "min M" column (steel 142, iron 65, bronze 43, copper 52, silver 34, bone 8, leather and wood 0.4) within 1 unit at Qa 0, and ours at Qa 1 is x1.2 of it.
- **RS-28** the DF2014 raws of steel, iron, bronze, copper, silver, gold, lead, tin, bone, leather and oak (density real), and their max edge.
- **RS-29** the human part sizes of the Material science table within 0.5%, and the relsize ratios exactly (the hand and foot within 3% until row 25's correction).
- **RS-30** slade alone deflects a DF war hammer (1,204 < 2,000; steel plate 78.5 does not), and the strain rule over every weapon metal (rigid at most 2% blunt, 0.7% edged; mail all).
- RS-9's strains are now named as the raws. RS-2 to RS-8 stay our table 4.4 (the wiki has no worked human cases).
- The **metal ranking** golden gained a bone breastplate (DF's bone, SF 0.13): with the raws' leather (SF 0.025) copper and bronze both cut a jerkin every time and neither cuts
  mail of its own tier, so the leather, bronze mail and iron mail set could no longer tell them apart.

## Calibration targets (resolver-v2 8; keep these)
- Duels last 10-20 s.
- 40-60% of attacks are defended.
- A spear beats a sword of equal skill at least 55% of the time.
- With treatment, bleed-out is at most 35%.
- Severing is rare; armour matters: mail turns cuts to blunt blows, rigid plate soaks 98-99% of a stopped blow.

## Notes (P4-A, open question Q1)
- The wiki still could not be read, so the goldens RS-1 to RS-8 stay **what our formulas give** (resolver-v2 4.4), as Q1 allows until the transcription task. The one wiki number we have, the battle-axe momentum shape, is checked: a dwarf (size 60,000, strength 1,250) hacking with a size-800 steel axe gives 92.862 (v1's double test and v2's integer `BaseMomentumMilli` agree), adamantine 68.181.
- rSY is kept in the edged formula (the decision above); the harness shows it matters little against cloth and decides the ranking against mail.
- The metal ranking golden holds: outer-piece cut rate silver 299 < copper 329 < bronze 333 < iron 341 < steel 674 permille over a seeded spread of sword slashes against leather, bronze mail and iron mail (`MetalRanking` test; `tools/Ravensguard.Calibrate` prints it).
- Sharpness enters as the material's max edge S (metal 1000 pm, wood and bone 100); no blade material (obsidian, the 10x case) exists in content yet.

## Notes (Phase 4 close-out)
Two departures from the DF rules as we read them, both found by the weapon x armour matrix (`docs/calibration/weapon-armour-matrix.md`): the light-weapon deflection is tested
only against a rigid shell (DF tests every layer, so a wooden club bounced off a linen shirt), and an edge or point that fails a rigid shell skids off it by the strain rule (DF
tests it blunt at its own contact area, so a dagger's point crushed a breastplate like a war hammer's face). DF's momentum formula, which all but ignores the weapon's mass, is kept;
the maul's velocity multiplier (1.7) carries its weight instead.

## Notes (the wiki re-check, Phase 4 close-out)
- **What changed in play.** The Qw fix touches no fight (no artifact weapon exists). The materials do, a little: leather needs about 15% less to cut, iron mail and helms
  about 0.6% more to crush, a wooden spade or pitchfork cuts a little better. Every calibration table was rerun (`tools/Ravensguard.Calibrate`, 2,000 duels per pairing): the
  **P4-C duel table, the ranged table and the mounted table are identical, row for row**, to the close-out's (sword vs sword padded 14.8 s, 52% defended, 50%; spear vs
  unarmoured sword 62%, padded 59%; mace vs plated sword 62%; full plate 23.1 s; all knocked out 29.6% / 5.4% bled out untreated / with first aid), so the resolver-v2 8
  targets stand where the close-out left them. The matrix (`-- --matrix`) moves by a point in the leather column (an edge's momentum reaching the body 88 -> 89%, a broadhead
  81 -> 82%, a thrown dagger 86 -> 87%) and for the axes against mail (the battle axe 84 -> 89%, with padding 44 -> 46%, through plate 2 -> 3%: iron's slightly higher crush need
  turns more hacks into a stopped blow, which mail passes at x1 where a crushed one keeps 0.95); the cut and bruise shares and the times to down are unchanged.
- **The metal ranking** (now with a bone breastplate in the set): silver 251 < copper 287 < bronze 303 < iron 361 < steel 714 permille.
- **The hand and foot (row 25).** Tried and held back: with DF's 1.44% and 2.16% the P4-C table was the same within a point, but AB-3's hard check on scenario A's casualties
  (abstract within 10% of the live fights) failed at 11.5% (9.2% before this work, 9.9% with the material fixes alone). Refitting the abstract model belongs
  to its own work (Sim/Combat/Abstract), so the sizes wait for it.
- **Hashes re-pinned, and why:** RS-24's 100,000-blow table hash (`2852731875240228929`: the materials are inputs to its blows). The abstract wound tables were rebuilt (`-- --abstract tables`, content hash `51660CE79CF95FD6`) and the AB-3 reference refought (`-- --abstract reference`):
  AB-3 has 17 known misses, as before (the ratchet is 17).
