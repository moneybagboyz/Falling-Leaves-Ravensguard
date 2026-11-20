# Family and lifespan: courting and arranged marriage, dowries and alliances, children raised as heirs, kin who can turn, and the hero's ageing

Plan refs: locked decisions (Nov 2026: full family life for the hero: courting or an arranged marriage with dowries and alliances; children born and growing up over game years, raised and trained as heirs; spouses and children are real people who can be kidnapped, married off for alliance, or turn against you; **a real lifespan on the slow clock**: the hero ages, strength fades in later years, old age can kill, which hands play to the heir you raised), §4A.2 (vital rates per game year; marriage gated on a dwelling; fertility 30% a year for married women 18-44; pregnancy 45 game days), §4A time compression (life events at historical per-year rates per game year: a 60-year life is about 180 days of play at 1x), §3j (the overlord's marriage and wardship rights), Phase 5 `inheritance.md` 4.5 (children born to the hero enter the order with no change; dowries are transfers at the wedding).

## 1. Purpose
Give the hero a household that is his own: someone to court or be matched with, a wedding that moves coin and binds houses, children who are born, grow a year every 60 game days and learn what he has them taught, and kin whose loyalty depends on how they are treated. Give the hero a body that ages like everyone's, so a long campaign ends with the heir he raised. The NPC side of estates left by Phase 5 (wills, minors' guardians, creditors) comes with it.

## 2. Dependencies
| Needs | From |
|---|---|
| `PersonTable` (`Spouse`, `Father`, `Mother`, `BirthDay`, `PregnantUntilDay`), `Marriage` (cluster matching, `FreeDwelling`, dowry on moving), `Demography`, `VitalRates` | Phase 2 |
| `HeroPerson` (the hero's row, `PersonStatus.Hero`, skipped by NPC vital rolls), `HeroRecord` attributes | Phase 5 |
| `EstateSettler`, `Wills`, `SuccessionDesk`, stream 0x561 (reserved: disputed inheritances) | Phase 5 |
| `RelationTable` (trust, familiarity, `Kin`, `Lover`) | Phase 3 |
| Standing; the church for weddings; the overlord's marriage right and wardship | `reputation-and-standing.md`, `faith-and-church.md`, `rule-and-income.md` |
| Kidnappers (bandit style), ransom | Phase 4 |
| Room `Owner` and `Assigned` beds | `holdings-and-works-v2.md` |
New: `Sim/Family/` (`Courtship`, `Matches`, `Weddings`, `Upbringing`, `KinLoyalty`, `HeroAgeing`), content `society/family.jsonc`. Extended: `Marriage` (the hero and arranged matches use its housing gate), `EstateWorld` (NPC wills, guardians, creditors), `SuccessionDesk` (the heir's own clothes, the crawl's haul).

## 3. Data
- **`Courtship`** (saved, optional section `family`): `{ Suitor, Courted, StartDay, Gifts (mites given), Meetings, Consent (the head of the courted's household: Pending, Yes, No), State (Courting, Betrothed, Wed, Broken) }`.
- **`Match`** (arranged): `{ HouseA, HouseB, PersonA, PersonB, DowryMites, BridePriceMites, Alliance (bool), WeddingDay }`.
- **`Upbringing`** per child of the hero's household: `{ Child, Tutor (person or -1), Course (Arms, Letters, Trade(skill), Church, Household), Hours }`.
- **`KinLoyalty`**: per kin member of the hero, the trust in the hero from `RelationTable` plus `Grievances` (bit flags: neglected, harshly punished, passed over, married off against will, a sibling favoured).
- **`HeroAgeing`** (hero section, additive key): last ageing day, attributes lost to age.
- **`society/family.jsonc`**: courtship weights, dowry shares by class (peasant 5%, burgher 10%, gentry 20% of the bride's household movables; bride price instead where the culture says), wedding fee 100 mites to the priest, ageing table (4.6), tutor hours per skill point.

## 4. Rules

### 4.1 Courting (the hero)
Any unmarried adult of marriageable age (culture `marriage_age`) and not kin within degree 2 (Phase 2 rule) can be courted: Q > **Court**. Each meeting (talk topic `court`, at most one a day) adds familiarity and trust by the Phase 3 talk rules; a gift adds `trust + value / (household coin / 100)` pm, capped at +100 a gift. **Acceptance score** = `trust / 10 + 20 - 30 x |class difference| + 10 x sign(wealth ratio - 1) + standing(settlement) / 20 + Range(-10, 10)` (stream **0x640**, key courtship and day). At >= 60 the courted says yes, then the **head of her (or his) household consents** with the same score using the head's trust and the house's standing; gentry heirs of a fief also need the overlord's consent (`rule-and-income.md` 4.2 marriage right). Betrothal -> a wedding at the church on the next holy day at least 5 days away (priest's fee 100 mites, `TransferReason.Fee`), refused if either is excommunicated.

### 4.2 Arranged marriages (the hero, his children and wards; NPC houses)
The head of a household (the hero) opens **Arrange a match** for himself or any unmarried member: candidates are persons of the same or adjacent class in the realm whose household heads consider it: `utility = 30 x alliance value (both houses gentry and the heads' lords differ) + standing / 10 + opinion between heads (diplomacy) + 0.5 x dowry offered / 100 crowns - 20 x age gap over 10 years`; accepted at >= 50. The dowry (or bride price) is negotiated within +-50% of the class share and moves at the wedding (`TransferReason.Dowry`, appended) from the bride's household to the groom's. Between two lords' houses the match makes a **marriage alliance** (`diplomacy-and-ai-lords.md`: opinion +30 each way, a treaty). NPC lords arrange their children's matches by the same function each S6 (Phase 2's matching keeps the commoners).

### 4.3 Where they live
The European marriage pattern holds (Phase 2): the couple needs a dwelling with a free bed; for the hero his household's dwelling or one he holds (Phase 5 deed); otherwise the wedding waits. The spouse joins the hero's household (row and purse), becomes kin (`RelationFlags.Kin`) and, by default, follows the household's schedule at home (not the party) unless recruited.

### 4.4 Children
The hero's married couple conceives by the Phase 2 rule (a married woman of 18-44: 30% a game year scaled by food security; pregnancy 45 game days; the Phase 2 conception stream keyed on the mother), whichever of them is the hero. Birth: a person row with `Father`, `Mother`, the household, the culture's naming (patronymic where used); the age-0 death rate applies (20% in the first year, Phase 2), plus the mother's childbirth risk (1% a birth: `DeathCause.Childbirth`, appended). A child ages one year every 60 game days and is playable as heir at 16 (12-15 with a guardian, Phase 5).
- **Upbringing:** the head sets a course and a tutor (a household member, a hired tutor at 350 mites a day, the priest for Letters free with standing >= 300, a master in a trade): from age 7, each game day of lessons (4 hours) gives the child `0.25 x (1 - s/100)^2` skill points in each of the course's skills (the Phase 4 learning curve, one lesson day counted as 0.6 of a meaningful use), and attribute training +0.02 a day for Arms. Untaught children learn their household's trade at half that rate from 10.
- **Coming of age:** at 16 the child chooses (by trust and the course) to stay as a member, take a trade, or leave (a son with trust < -300 and an ambition leaves).

### 4.5 Kin who can be taken or turn
- **Kidnapping:** a band of style `kidnapper` within 10 km of the hero's home picks a target among his kin who are away from guards with P = 4% a season x (renown / 500) (stream **0x643**, key hero and season); the kin becomes a captive held for ransom (Phase 4 ransom rules: price by rank, the hero's household pays) or sold (`slavery.md`) after 10 days. A rumour and a rescue contract follow.
- **Married off for alliance:** the head can arrange a match for any unmarried member (4.2). A member married against his will (his own acceptance < 30) gets the grievance `married_off`.
- **Turning against:** a kin member's loyalty = trust in the hero (pm) - 150 x grievances. Below **-300** a yearly check (stream **0x642**, key person and year) may make him: leave the household (P 30%); refuse a match (always); **contest the inheritance** at the hero's death (an adult son passed over by a will: the dispute uses the reserved stream **0x561**: the court of the deed's lord decides by law vs will, standing of each claimant); side with a rival claimant or a rebel (P 10% if one exists); tell kidnappers where the family is (P 5% if a kidnapper band is near). Every turn is a chronicle entry with the grievances as causes.

### 4.6 The hero's ageing (the slow clock)
The hero's row is aged like everyone's (it already holds `BirthDay`). On the hero's birthday, attributes fall from these ages (points on the 0-100 attribute scale, baseline 50): strength -1 every second birthday at 40-49, -1 a year at 50-59, -2 a year from 60; toughness -1 a year from 45, -2 from 60; dexterity -1 a year from 45; perception -1 a year from 50; wits -1 a year from 65. Training still adds (+0.02 a day of matching activity, Phase 4), so an active old soldier fades more slowly. Skills do not fall (practice still raises them). **Old age can kill:** a yearly hazard (only old age; illness is `disease-and-plague.md`, wounds are wounds): 0 below 50, 1% at 50-59, 3% at 60-64, 10% at 65-74, 20% from 75 (stream **0x644**, key hero row and year); a warning a season before the roll once the hazard is >= 3% ("Your hands shake in the cold mornings"). Death by old age is `DeathCause.Age` with the chronicle cause "old age at 68", and succession runs (Phase 4-5). Companions age the same; NPCs keep the Phase 2 vital table.

### 4.7 Estates of NPCs and the heir's details (Phase 5 carry-overs)
- **NPC wills:** a head of household with property worth >= 5,000 mites and a disliked heir (trust < -300) or no kin makes a will at the church (Phase 5 will rules) with P 20% a year.
- **An NPC heir under 16** with a business: the nearest adult kin runs it as steward with default directives until 16 (Phase 5 4.4 for NPCs).
- **A creditor sells a claimed business** at S6 when its new household cannot pay the staff for 15 days (Phase 5's open item).
- **The heir's clothes** are his own rows (he takes up the hero's id but keeps his wardrobe; nothing is made new by class). **A hero who dies below ground:** the crawl's haul is moved to the estate chest at the site's mouth, not left in the crawl.

### 4.8 Tick placement
| Where | What |
|---|---|
| Barrier | courting talk and gifts, betrothals, arranging, weddings, upbringing settings |
| H4 slice | lessons (a day's hours), pregnancy and births through the Phase 2 vital rolls (the hero's household included) |
| H5 hour 0 | the hero's birthday: ageing and the old-age roll; kin loyalty checks on their birthdays; kidnapping at a season's first day |
| H6 S6 | NPC lords' arranged matches; NPC wills |

## 5. Determinism
Streams: **0x640** courtship acceptance, **0x641** a child's starting attributes (parents' mean +-2, key child row), **0x642** kin turning, **0x643** kidnapping, **0x644** the hero's old-age roll; 0x561 (Phase 5 reserved) now drawn for disputed inheritances. Ties by person id. `FamilyHash` covers courtships, matches, upbringing and loyalty grievances.

## 6. UI (960 x 540)
Character screen (C) > **Family** tab (Phase 5's kin list extended): spouse, children with age, course, tutor, skills, loyalty band and why; Court and Arrange buttons; a match panel (candidates, utility terms shown in words, dowry slider). The HUD shows the hero's age; the Character screen shows what age has taken ("Strength 14 (-3 with age)").

## 7. Tests (golden)
| id | Case | Expected |
|---|---|---|
| FL-1 | Courting score | trust 500, same class, equal wealth, standing 0, jitter 0: 50 + 20 = 70 -> yes; one class apart: 40 -> no |
| FL-2 | Consent and wedding | the father's consent, a wedding on the next holy day >= 5 days away, priest paid 100 mites, the bride joins the hero's household, dwelling gate checked |
| FL-3 | No dwelling | a betrothal waits until the hero holds a dwelling with a free bed |
| FL-4 | Dowry | a burgher bride's household with 30,000 mites of movables: dowry 3,000 at the wedding; coin conserved |
| FL-5 | Alliance | the hero (a lord) marries his daughter to a neighbouring lord's son: opinion +30 both ways, a marriage treaty in the diplomacy book |
| FL-6 | Overlord's right | a fief heiress's match without the overlord's consent: refused; with his fee paid: allowed |
| FL-7 | Children | a married hero couple over 10 game years: births within the Phase 2 rate (0.3 a year scaled), every child a row with both parents set, born into the household |
| FL-8 | Upbringing | a child taught Arms by a tutor from 7 to 16 (540 lesson days): Blades 57 +-1 at 16 (`1/(1 - s/100) = 1 + 0.0025 t` from 0); untaught from 10: 31 +-1 in the household trade |
| FL-9 | Ageing | a hero of strength 50 who turns 40 on day 0, no training: strength 42 at 52 (-5, -3), 13 at 70 (-5, -10, -22); toughness 50 -> 25 at 64 (-15, -10); the sequence for 40-75 equals the content table |
| FL-10 | Old age death | 10,000 heroes from 50: survivors at 65 within +-1.5 points of the hazard table's product; a death runs succession to the heir |
| FL-11 | Kidnapping | renown 500, a kidnapper band near: 4% a season +-0.5 over 10,000 seasons; a ransom asked at the Phase 4 rank price |
| FL-12 | Turning | a son with trust -400 and 2 grievances (-700 loyalty) leaves with P 30% a year; passed over by a will, he contests at the hero's death (0x561) and the deed's court decides |
| FL-13 | NPC carry-overs | an NPC heir of 13 inherits a smithy: his uncle runs it as steward until 16; a creditor sells a claimed business after 15 unpaid days |
| FL-14 | The heir's details | the heir keeps his own clothes; a hero dead at z -2 leaves the haul in the estate chest at the mouth |
| FL-15 | Save mid-courtship, mid-pregnancy, mid-lesson | save -> load -> save byte-identical; `FamilyHash` equal |

## 8. Open questions (recommended defaults)
1. **Ageing pace** (overview Q1): historical, the same clock for everyone (a 60-year life is about 180 days of play at 1x; a child comes of age in about 48 days of play at 1x, 6 at 8x). No ageing slider. Heirs of 12-15 are playable with a guardian (Phase 5).
2. **Same-sex marriage, polygamy:** not in the base cultures (historical); the culture fields allow a mod to add them. Default as written.
3. **Bastards:** a child outside marriage (the `Lover` flag) is a row with a father set and a `Bastard` flag; last in the law's order unless legitimised by the church (a fee). Default as written.

## 9. Budgets
Courtship and match utilities <= 0.05 ms each; lessons <= 1 us per child an H4 slice; ageing and loyalty <= 0.1 ms a day; NPC matches <= 3 ms at S6 a region; save: courtships 24 B, upbringing 16 B per child, loyalty 4 B per kin.
