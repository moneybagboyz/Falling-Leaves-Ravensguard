# Faith and the church: generated religions, saints and feasts, relics and pilgrimages, clergy and church land, piety, heresy and excommunication, superstition

Plan refs: locked decision (Nov 2026, about +1-2 months: generated religions per culture, with saints, feast days, relics and pilgrimages; clergy are people, and the church holds land and collects tithes; piety and reputation with the church; heresy and excommunication as political tools; superstition tied to the rare monsters (§3b); history v0 recalibrated to include it), §3j (the church: tithes to the parish; clerical taxes and the income of vacant bishoprics are a ruler's windfall), §4A.2 (cathedral -> chapel, parish church, minster), §3b (barrows are a taboo; followers with the *superstitious* trait lose morale in the dark), locked "Fantasy" row (mostly grounded, rare monsters; superstition still shapes behaviour).

## 1. Purpose
A church that is an institution of people, land and coin, with a calendar the whole region lives by, a faith the player can keep or break, and a hierarchy that rulers use against each other. Phase 1 already has culture feast days and history saints with reliquaries; Phase 2 has tithes into a parish chest and `Occupation.Priest`. This spec generates one religion per culture (or shared), organises parishes, abbeys and bishoprics as holders of land and treasuries, gives every person piety, and adds heresy, excommunication, pilgrimage and superstition. **Grounded:** no miracles (overview Q4); faith acts through people.

## 2. Dependencies
| Needs | From |
|---|---|
| Culture `feast_days`, months; `DayKind.Holy`; `ActionKind.Worship`; `Skill.Theology`; `Occupation.Priest`; parish treasuries (`TreasuryKind.Parish`), tithes (`TransferReason.Tithe`), parish relief | Phase 1-3 |
| History saints (`NotableRole.Saint`), reliquaries (artefacts), plague and famine saints | Phase 1 history |
| Named artefacts (a relic is one) | Phase 5 items |
| `LandRegistry` holders (glebe parcels, `OwnerKind.Glebe`) | Phase 5 |
| Standing groups (`Religion`), marks (`Excommunicated`) | `reputation-and-standing.md` |
| Crimes `heresy`, `sacrilege`, `grave_robbing`; court benefit of clergy | `crime-and-justice.md` |
| Monster lairs and rumours, `superstitious` trait | Phase 4 |
New: `Sim/Faith/` (`FaithSeeder`, `Religion`, `ChurchBook` (parishes, abbeys, sees), `Piety`, `Heresy`, `Pilgrimage`, `Superstition`), content `faith/religion_templates.jsonc`, `faith/doctrines.jsonc`. Extended: tithes (the split), `Occupation` (+`Monk`), `HoldingKind` holders (church), room types (`nave`, `altar` pieces: chapel exists).

## 3. Data
- **`Religion`** (derived, never saved: a pure function of the world seed, the cultures and the history; overview 7): `{ Id, Name, Culture(s), DeityTerms (from the template: "the Allfather", "the Light"), Saints[] (history saints of its cultures plus 3-5 founders named at world creation), HolyDays[] (the culture's feast days + one day per saint, at most 12 a year), Doctrines (below), HolySite (the shrine of its greatest relic: a settlement), Head (the patriarch: a person row in the continent's records) }`. Base templates: four, one per base culture, with tones from the culture (Northmark's old gods and blots, Valeholt's saints and minsters, Highmarch's hill saints and holy wells, Seaboard's sea saints and the Drowned). Two cultures may share one religion by template (`religion_templates.jsonc`: `cultures: [...]`).
- **Doctrines** (`doctrines.jsonc`, per religion): `slavery_stance` (forbid, coreligionists_forbidden, accept), `usury` (forbid: believers may not lend at interest; loans then come from others), `fasting` (no meat on 1 day in 5: household demand shifts to fish and pulses), `barrow_taboo` (0-100: the standing cost of robbing the dead), `church_courts` (benefit of clergy), `clergy_marry`, `tithe_share` (parish, see, fabric and poor: default 1/3 each).
- **Church holders and offices** (saved, optional section `faith`): every parish (a settlement with a chapel or church: priest, glebe, chest), each **see** (one per realm part in the region: a bishop at the largest town's church, which becomes a minster), **abbeys** (from history: a ruin-free abbey site with 10-30 monks, `Occupation.Monk`, appended; their estates are deeds held by the abbey). Each office holder is a person; vacancies are recorded with the day.
- **`Piety`** per person, 0..100 (saved only where it differs from the derived default: class and traits give 40-70).
- **Heresy** state per settlement: adherents (count) of a named heresy, its preacher (a person), the day it began.
- **Superstition** per settlement, 0..100 (saved).

## 4. Rules

### 4.1 Holy days and worship
On a holy day nobody works who can help it (`DayKind.Holy`: Phase 3 schedules), people attend the parish service (Worship) and the parish feasts: household food demand that day +50%, fairs on the great feasts (Phase 3 fair days). Attending a service: piety +1 (at most one a holy day). Breaking a fast or working on a holy day where seen: piety -1, standing with the religion -5 (a deed).

### 4.2 Tithes and church income
Tithes stay the Phase 2 rule (10% of the harvest split, or the culture's `tithe_percent`, into the parish chest); Phase 6 splits each season's tithe receipts by `tithe_share`: the parish keeps its third (the priest's living, alms through parish relief), the **see** gets a third (the bishop's treasury), the fabric-and-poor third stays in the parish chest earmarked for the church fabric (repairs, a church contract when the chest holds the cost) and alms. Church lands (glebe, abbey estates) are farmed and let like any deed (Phase 5); their rents go to the holder's chest. A ruler may ask a **clerical tax** (a tenth of a see's receipts for a season) when the bishop's opinion of him >= 0, and takes a vacant see's receipts until a new bishop is chosen (1-2 seasons: the chapter elects the most pious senior priest of the realm; the ruler may push his own candidate with a gift >= 50 crowns).

### 4.3 Piety and standing with the church
Piety moves by deeds (content rows in `society/deeds.jsonc`, `reputation-and-standing.md`): alms +1 per 100 mites given, a pilgrimage completed +10, venerating a relic +2 (once a season), sins: murder -20, theft -5, sacrilege -30, owning slaves against the doctrine -10 a season, grave robbing -(barrow_taboo / 5), heresy -30. The church's view of a person is standing with the religion group, whose deeds include piety changes x10. **Effects:** a priest marries, buries (the Phase 4 funeral) and witnesses wills only for standing > -500; benefit of clergy needs standing >= 0; piety >= 70 gives +10 to morale in the dark for a `superstitious` follower who fights beside a pious leader; a lord's piety and his bishop's opinion feed his vassals' and subjects' opinions (+-10).

### 4.4 Relics and pilgrimages
Relics are named artefacts with the `relic` kind (history reliquaries and saints' bones, Phase 5 named items): held by a church or abbey (on its altar), stolen, buried, or for sale. A **pilgrimage** is a journey to the religion's holy site or any shrine with a relic: taking it (Q to a priest > Pilgrimage) gives a pilgrim's badge on arrival and piety +10, standing +80 with the religion, renown +5; a party of pilgrims on the road is a road encounter (Phase 3: travellers) and a contract to escort (`contracts-and-rumours-v2.md`). Grounded: the sick who reach a shrine feel better (pain -10 for 5 days) and nothing more. **Fake relics** are sold by pardoners (a Wits 60 or Theology 30 check spots one); a church that buys a fake loses standing when exposed. Stealing a relic is `sacrilege` (crime, excommunication risk).

### 4.5 Heresy
A heresy starts in a settlement where a priest or a monk with Theology >= 50 and piety >= 80 has a grievance with the bishop, or a plague or famine has struck (stream **0x652**, key settlement and season, P 2% then); it has a name and one doctrine changed (e.g. `tithe_share` to the poor only, or `slavery_stance` forbid). Adherents grow by rumour: each hearing converts the hearer with P = 10% + 2 x his grievances (unrest) - piety / 5 (0x652, key person, draw 1). The bishop, when he hears of it, may (by his traits) preach against it (adherents -20% a season), ask the lord to try the preacher (the crime `heresy`: the culture's sentence, e.g. Valeholt penance; a relapsed heretic: the church court's sentence, overview T5), or declare the whole settlement under interdict (no services: unrest +2 grievances a household). A heresy with > 50% of a fief's people can make the fief's lord switch (if his own piety < 50 and it serves him: diplomacy utility), splitting the see.

### 4.6 Excommunication (a political tool)
The bishop (or the head of the religion for a ruler) excommunicates a person whose standing with the religion falls below -800 while his sins continue, or **on a ruler's request**: a ruler with the bishop's opinion >= 50 may ask for a rival lord's excommunication on a cause from the chronicle (an attack on a church, a heresy, a broken oath, the seizure of church land), at a gift of 100 crowns. Effects on the excommunicated: the mark; no church services (no wedding, no burial in holy ground, no will witnessed by a priest), standing with the religion -1000; **his vassals and subjects are released from their oaths**: each vassal's fealty utility drops by 40 (`diplomacy-and-ai-lords.md`), his fiefs' households +2 grievances (`unrest-and-revolt.md`); it is a casus belli for any ruler of the religion. Lifted by **penance**: a payment of 10% of his treasury (or household coin) to the see and a pilgrimage, or a new bishop's grace.

### 4.7 Superstition and the rare monsters
Superstition per settlement starts from the history (a lair within 10 km: +30, a plague in living memory: +20, a cursed barrow: +15) and rises by rumours of monsters (+10 per monster rumour heard by 50% of the settlement), unexplained deaths and eclipses of order (a great plague +30), falling 5 a season otherwise. Effects: at >= 40 villagers avoid the night roads and the barrow (schedules), buy charms (good `charm`, 20 mites, from the priest or a cunning woman), ask the priest to bless fields and doors (a holy-day rite); at >= 70 a bad harvest or a death can start a **witch hunt** (a scapegoat accused: a crime trial with no real witness; the bishop may stop it); `superstitious` followers lose morale in the dark as in Phase 4, more at >= 60. A monster slain and its head shown: superstition -40, renown and standing with the settlement and the religion up.

### 4.8 Tick placement
| Where | What |
|---|---|
| Seeding pass (overview 7) | religions derived; parishes, sees and abbeys bound to persons and holders; piety defaults |
| H5 hour 0 | holy-day services and feasts, fasting demand, heresy spread from the day's hearings, superstition from rumours |
| H6 S4 (with taxes) | the tithe split, clerical tax, vacant sees |
| H6 S6 | bishops' decisions (preach, try, interdict, excommunicate), elections |
| Barrier | the hero's alms, pilgrimages, relic trades, penance |

## 5. Determinism
Religions are derived from the world seed and history with the history's own streams (`history-recalibration.md`: the faith founders and names use the generator's appended stream ids; for library worlds made before Phase 6, `FaithSeeder.FromCultures` uses stream **0x650**, key culture id). Draws: **0x651** founders' names and holy-day placement for derived religions (key religion, draw), **0x652** heresy starts and conversions, **0x653** pilgrim road events (key party, day), **0x654** witch hunts (key settlement, season). `FaithHash` covers offices, piety deltas, heresies, superstition and relic holders.

## 6. UI (960 x 540)
A **Faith** tab in the Character screen: the hero's religion, piety and its band, standing with the church and why, sins and good works of the last year, holy days ahead. The Codex gains the religion's page (saints, holy days, doctrines, holy site). The Faction screen shows bishops and their opinions. Churches show relics on their altars (inspect: name, history line).

## 7. Tests (golden)
| id | Case | Expected |
|---|---|---|
| FC-1 | Seeding, seed 1234 | one religion per base culture with 3-5 founders + the history saints of its cultures, at most 12 holy days a year, doctrines from the template; the same on a new game and on a v8 save loaded |
| FC-2 | Holy day | no field or workshop hours that day; household food demand +50%; services attended by >= 70% of adults; the hero's attendance +1 piety once |
| FC-3 | Tithe split | a parish receiving 3,000 mites of tithe in a season: 1,000 kept, 1,000 to the see's treasury, 1,000 earmarked for fabric and alms; coin conserved |
| FC-4 | Vacant see | the bishop dies: the ruler's treasury receives the see's receipts until the election 1-2 seasons later; chronicle entries with causes |
| FC-5 | Pilgrimage | the hero walks to the holy site: piety +10, standing +80 with the religion, a badge item; a sick pilgrim gets pain -10 for 5 days and no cure |
| FC-6 | Relic theft | taking a relic from an altar while seen: `sacrilege`, piety -30, standing -300 with the religion |
| FC-7 | Heresy spread | a heresy seeded in a town of 1,500 with 200 aggrieved households: adherents after one season within the conversion rule's expectation +-10%; the bishop's response by trait |
| FC-8 | Excommunication by request | ruler A with the bishop at 60 pays 100 crowns, cause "seized the abbey of Wend, day 140": lord B excommunicated; his vassals' fealty utility -40; his fiefs +2 grievances a household; a casus belli for A |
| FC-9 | Penance | B pays 10% of his treasury and completes a pilgrimage: the mark cleared, services restored |
| FC-10 | Services refused | standing -600: no wedding at the church (FL-2's wedding refused), no burial in holy ground |
| FC-11 | Superstition | a lair within 10 km and two monster rumours heard by 60%: superstition >= 50; villagers' night schedules avoid the barrow road; killing the monster and showing its head: -40 |
| FC-12 | Slavery doctrine | owning a slave under `forbid`: SL-6's piety and standing changes |
| FC-13 | Usury | a believer under `forbid` cannot offer a loan at interest; loans in that realm come from merchants of another faith or none (`rule-and-income.md` RI-14) |
| FC-14 | Determinism and save | `FaithHash` equal across runs; save -> load -> save byte-identical with a heresy, an excommunication and a pilgrimage under way |

## 8. Open questions (recommended defaults)
1. **Miracles** (overview Q4): none; relics and pilgrimages act on people's minds, standing and morale only.
2. **Burning relapsed heretics** (overview T5): only where a culture's table says so (none of the base four: Valeholt's heresy is penance, a relapse exile); a mod culture may add it.
3. **One religion per culture or shared:** one per base culture; templates may share. Default as written.

## 9. Budgets
Holy-day services <= 2 ms at H5 a region on a holy day; heresy conversions <= 0.5 ms a day (only hearings of the heresy's rumour); seasonal church decisions <= 2 ms; derived religions at load <= 20 ms; save: offices and piety deltas <= 40 KB.
