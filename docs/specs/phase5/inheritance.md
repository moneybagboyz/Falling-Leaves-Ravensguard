# Inheritance of property: the hero as a person, estates, debts, wills, and the end of Ironman succession

Plan refs: §3.7 (with Ironman you continue as a companion or heir, who inherits property and reputation; the run ends only when nobody is left, and the world persists), §4A.2 (kinship links support inheritance and Ironman heirs; inheritance goes eldest son -> children -> spouse -> back to the lord (escheat)), §5 Phase 4 ("Ironman succession by kinship. Property passes over fully in Phase 5, reputation in Phase 6"), §5 Phase 5 ("Inheritance of property completes Ironman succession"), §5 Phase 6 (family life for the hero feeds succession and inheritance; vassal dues include relief, wardship, marriage, escheat; systemic slavery), locked decisions (a real lifespan on the slow clock: old age hands play to the heir you raised; rare great plagues that kill a third of a town).

## 1. Purpose
Phase 4 picks the heir (`SuccessionOrder`, a pure function: children 16+, spouse, siblings 16+, children 12-15 with a guardian, parents, companions, followers), moves the body's coin less a 5% funeral, leaves items on the corpse, and records deeds and reputation as pending (`InheritanceRecord.DeedsPending`, `ReputationPending`). It could not do more because **the Phase 4 hero is not a person row** (morale-capture-succession Notes) and nothing was deeded. Phase 5 makes the hero a person with kin, settles the whole estate (deeds, businesses, shares, sites, leases, vehicles, animals, stores and debts) through one rule shared with every NPC who dies, and switches play to the heir in the running game.

## 2. Dependencies
| Needs | From |
|---|---|
| `SuccessionOrder`, `Estate`, `InheritanceRecord`, `SuccessionBook` (`EstateHash`), the succession screen, stream 0x457 reserved | exists (Phase 4) |
| `PersonTable` (`Father`, `Mother`, `Spouse`, `BirthDay`, `Status`), `HouseholdTable`, `KinGraph`, `Demography` deaths, Phase 2 "heirs of a dead owner with the same trade continue it" | exists |
| `HeroRecord` (attributes, skills, items, coin), `HeroParty`, `Squad` loyalty | exists |
| Deeds and claims | `land-and-deeds.md` |
| Businesses, shares, stewards | `businesses-stewards-ledger.md` |
| Construction sites, vehicles | build spec, `vehicle-designer.md` |
| `Ledger` reasons `Inheritance`, `Escheat`, `Tithe` (funeral) | exists |
New: `Sim/Succession/EstateSettler` (one function for hero and NPC), `Sim/Succession/Wills`, `Sim/Hero/HeroPerson` (the link). Culture content gains `inheritance_law`.

## 3. Data
- **The hero as a person row:** at a new game, `HeroPerson.Insert` adds the hero to `PersonTable` (sex, birth day from the origin's age, attributes and skills copied from `HeroRecord`, occupation from the origin) in a household of his own at the start settlement (no dwelling: `DeedKind.None`) and, by origin, real kin: the tanner's widow has a dead husband's row and her children in the town; the disgraced squire has a living father at a manor; the deserter none (content `origins.jsonc` gains `kin: [{ "rel": "child", "age": [6, 14], "count": [0, 2] }, ...]`, rolled with stream **0x560**, key hero seed). `HeroRecord` stays the hero's authoritative stats; the row mirrors them at each barrier (one direction, hero -> row). Recruited companions are already rows.
- **`Estate`** (Phase 4) gains `Deeds[]`, `Businesses[]`, `Shares[]`, `Sites[]`, `Leases[]`, `Vehicles[]`, `Animals[]`, `Stores[]` (store ids the dead held: home chest, barn, business stock is the business's), `DebtsMites` (owed wages, rent arrears, commission balances), `Heirs[]` (by share).
- **`InheritanceRecord`:** `DeedsPending` becomes `Deeds` (deed ids passed); a v7 record's empty list migrates as empty; `ReputationPending` stays for Phase 6.
- **`Will`** `{ Testator, Beneficiaries [(person, what: all | deed | business | coin pm)], Witness (priest or reeve), Day }`: made in person at a church or the reeve (dialogue `make_will`), fee 200 mites (2 crowns) to the witness's purse; one per person, replaced by a newer one.
- **Culture law** (`cultures.jsonc`): `inheritance_law: "primogeniture"` (the plan's order, default for every base culture) or `"partible"` (real property split among sons, eldest first by value; content for later cultures and mods).

## 4. Rules

### 4.1 Who inherits property (`EstateSettler.Heirs`)
1. **A will** names who gets what (any person, kin or not, a business partner, the church). What it does not name follows the law.
2. **The law** (primogeniture, plan 4A.2): eldest living son 16+, then the other children by age (daughters after sons), then the spouse, then children 12-15 (a guardian manages: 4.4), then siblings, then parents; the rest of the plan's order ends with **escheat**: real property (deeds, claims) returns to the lord of each deed (`Deed.Lord`), movables (coin, stores, vehicles, animals, shares) to the lord too.
3. **The play heir** is still Phase 4's `SuccessionOrder` (it includes companions and followers). When the play heir is also the property heir (the usual case: a child or spouse), play continues with everything. When the play heir is **not kin** (a companion), he inherits what a will gives him, plus what he carries and what is on the body and in the party's packs (Phase 4 rule), and the rest goes by law (overview Q2).
The same function settles **every** person who dies holding a deed, business, share, site, lease or store: an NPC smith's smithy goes to his eldest son (Phase 2's "heirs with the same trade continue it" is now this rule plus the existing trade check: an heir not in the trade keeps the business as owner and the labour market hires a smith, or sells it at S6 if the household cannot pay the staff).

### 4.2 Settling the estate (order fixed, all transfers through the Ledger)
1. **Funeral:** 5% of the coin to the local church (Phase 4, reason `Tithe`).
2. **Debts:** owed wages to his workers (in ascending person index), rent and dues arrears to the lessors and lords, commission balances to makers, from the coin first; then from stores' goods sold at bid to the creditor directly; if still short, creditors may claim property at the next S6 in the order stalls, shares, vehicles and animals, businesses, deeds (lowest value first) until paid (each claim a transfer at valuation). What is left passes clean.
3. **Transfers:** deeds change `Holder` (`DeedChange` with cause `inherited`), businesses' owner and shares, sites' owner and payer (a site paused `Owner` resumes when the heir says so), leases continue to their end in the heir's name (he pays from then), vehicles and animals change owner, stores change owner in place (goods are not moved: the heir must go and get them), stewards stay employed by the heir with their directives.
4. **Partible law** splits real property among the eligible sons (each deed whole, eldest first by value, round robin), movables equally (remainder to the eldest).
5. **Chronicle:** `estate_settled` (cause: the death), with what went where; the heir's Ledger shows an "Inherited" banner on each row.

### 4.3 Switching play to the heir (closes the Phase 4 open item)
At the hero's death in Ironman (or "continue as heir" otherwise) the succession screen (Phase 4) now lists **what the heir receives** (a summary from `EstateSettler` before it runs: deeds, businesses, coin after funeral and debts, what escheats), then at the barrier: the heir's row becomes the hero (`HeroRecord.FromPerson`, Phase 4), the estate settles, the bubble re-centres on the heir (promotion spread over frames), followers by Phase 4's loyalty rule. The heir's ambition: the dead hero's ambition continues if the heir is kin (same family goal), else the heir's origin-less default ("find your place").

### 4.4 Minors and guardians
A property heir aged 12-15 inherits with a guardian (Phase 4: the most loyal follower; for NPCs the nearest adult kin): the guardian runs the property as a steward with the default directives, paid the steward wage from the estate's businesses, until the heir is 16. The player plays the minor heir (Phase 4) and can change directives; he cannot sell deeds until 16.

### 4.5 Later phases (compatibility)
- **Phase 6 family life:** children born to the hero are rows with `Father`/`Mother` set, so they enter the order with no change. Marriage portions and dowries are transfers at the wedding. **Relief** (the lord's fee on inheriting a fief deed) and **wardship** are Phase 6 vassal dues: `EstateSettler` has a hook `OnDeedInherited(deed, heir)` that Phase 6 fills; Phase 5 charges nothing.
- **Phase 6 slavery:** an enslaved heir is unavailable (skipped, as a captive is in Phase 4); a person is never a deed or an estate item in Phase 5.
- **Plague:** a great plague can settle hundreds of estates in a season: each settle is O(holdings) with no search over the world (holdings are indexed by holder), budget below.
- **Reputation** stays pending until Phase 6.

### 4.6 Tick placement
| Where | What |
|---|---|
| Barrier | hero death -> succession screen data, settle, switch; `make_will` |
| H5 hour 0 (daily) | NPC deaths from `Demography` settle in the same barrier as the death (ascending person id) |
| S6 | creditor claims on unpaid estates |

## 5. Determinism
Settling is a pure function of the tables (no draw): heirs by the fixed order and birth day, ties by id; debts in ascending creditor id. Streams: 0x457 (Phase 4) stays reserved; **0x560** the hero's kin at a new game only; **0x561** reserved for Phase 6 disputed inheritances, asserted never drawn. `EstateHash` covers the new estate lists.

## 6. UI
The succession screen (Phase 4) gains the "You inherit" list and "Goes to the lord" list with values; the Ledger's Property tab marks inherited rows; Q on a priest or reeve > Make a will (the will panel: beneficiaries picked from people you know, what each gets, the 2-crown fee); the Character screen shows the hero's family (kin rows: alive, age, where). Readable at 960 x 540.

## 7. Tests (golden)
| id | Case | Expected |
|---|---|---|
| IN-1 | New game as the tanner's widow, seed 7 | a hero person row, her household, the dead husband's row and 0-2 children with `Mother` = hero; same on both OS |
| IN-2 | Primogeniture: a dead smith with sons 24 and 19, a daughter 21, a wife | the smithy, the house deed and the shares to the son 24; the others nothing (no will) |
| IN-3 | Will: the same smith leaves the house to his wife | house to the wife, the rest to the son 24 |
| IN-4 | Hero dies, heir is his son 17, estate: 2 deeds, a business, 3,000 mites coin, 400 mites owed wages | funeral 150; wages 400 paid; 2,450 to the son; deeds, business, sites to the son; play switches to the son; coin and deeds conserved |
| IN-5 | Hero dies, play heir a companion, no will, no kin | the companion keeps his pack and the body's items and coin (Phase 4); deeds escheat to their lords; the business's ownership to the lord |
| IN-6 | Same with a will naming the companion | everything to the companion |
| IN-7 | Debts larger than coin: 500 coin, 2,000 owed, a stall lease and a shop | coin to creditors, then goods at bid; at S6 the stall lease and shares claimed before the shop |
| IN-8 | Minor heir 13 | guardian runs the business as steward; a deed sale by the heir is refused until 16 |
| IN-9 | Partible (a test culture): three sons, deeds worth 900, 500, 300, 100 | eldest 900 and 100, second 500, third 300; coin split equally, remainder to the eldest |
| IN-10 | NPC continuity: a smith dies, his son not a smith | the son owns it; the labour market hires a smith at the next H5; dividends to the son |
| IN-11 | Save mid-succession (screen open), load | the same heir and the same estate summary; `EstateHash` equal |
| IN-12 | Plague load: 1,500 deaths with holdings in one day | settled in <= 30 ms; coin, mass and deed counts exact |
| IN-13 | No randomness in settling | streams 0x457 and 0x561 never drawn |

## 8. Open questions (recommended defaults)
1. **Non-kin heirs** (overview Q2): a will makes a companion a full heir; without one he keeps what he carries and the law takes the rest.
2. **Daughters after sons** under primogeniture (the plan's "eldest son"): default as written; a culture can set `partible` or (Phase 6) `cognatic`.
3. **Hero's kin at the start:** by origin, small (0-2 children for the widow, a father for the squire). Default as written; Phase 6 family life adds the rest.

## 9. Budgets
Settle one estate <= 0.02 ms per holding (holder index lookups only); the summary for the screen <= 1 ms; save: wills 40 B, estate lists 8 B per entry; the hero person row adds one row to the people diff (about 1.5 KB).

## Notes (P5-A4 implementation, step 1: the hero as a person row and the real pack)
Code: `Sim/Hero/HeroPerson.cs` (`Insert`, `InsertForLoad`, `Mirror`, `RowOf`, `StartSettlement`), `Sim/Inventory/HeroPack.cs` (what the pack really holds, as kit items and as craft tools), `PersonStatus.Hero` (appended), `HeroRecord.PersonIndex` (hero section key 12), `HeroView.Family` (`KinView`), `OriginDef.Kin` / `Age` / `Occupation` (`OriginKinDef`), the v8 step in `SaveMigrator` (`MigratePhase5V8`) and its load half in `WorldSave.Restore`. Stream 0x560 registered. Tests: `tests/.../Hero/HeroPersonTests.cs` (IN-1 and its rules, the row in the save, the row at a death, CF-4, the loot test reopened), `Save/V7MigrationTests.cs`.

The row (3):
- **Id and status.** The hero's row carries, as its foreign id, the id the hero already had everywhere (`EconomyState.HeroOwner`, person counter 0, never allocated: the purse, the pack's store and item rows, the wound book, the squad), so nothing that keyed on the hero moved. Its status is `Hero`: every NPC system reads `Alive`, so the row is never hired, married, aged to death, shown as a live NPC, taxed as a villager or brought back as a returning emigrant (a Phase 4 heir's `Departed` row could have been). The row is the head of its household but not one of its eating members (the hero eats from the pack); kin are real members.
- **New game** (`NewGameWorld.Create`): the row is appended with a household of the hero's own at the start settlement (`StartSettlement`, the rule the game's region screen now calls too), no dwelling, no deed. Age from the origin's `age` (default 22-30), the occupation and class by origin, attributes and the four best skills mirrored from `HeroRecord` at every barrier (`HeroDesk.ApplyDue` and `AfterHour`; one direction). Ids stay in step with the tables (`EnsureNext` as a wedding does), the economy gets the rows' columns and the household's purse and store, a household with children gets a month of food like every household at world creation.
- **Kin by origin** (stream 0x560, keyed on the world seed and the hero's id): `spouse_dead` (a row dead before the game began, its `Spouse` the hero's; the widow's own link cleared as `Demography.Kill` does), `child` (alive rows in the hero's household, `Mother`/`Father` the hero and the dead spouse), `parent` (an existing living head of a household of the hero's class nearest the rolled age: the squire's father at a manor), `none`. An origin without `kin` keeps the Phase 4 rule (`HeroKin.Choose`), and the chosen parents become the row's `Father` and `Mother`. Base content: the disgraced squire has a father (gentry), the discharged soldier (the concept book's deserter) none, the other five the Phase 4 rule.
- **The tanner's widow** is not a base origin yet (the seven Phase 1 cards are the base; a new card needs its own ambition, first-hour script and the autoplay's eighth run). IN-1 loads her as a test mod (`tanners_widow`: a dead husband, 0 to 2 children of 6 to 14, seed 7), so the rule is content and the card can be added as content later.
- **A death** (`SuccessionDesk`): the dead hero's row is `Dead` (its household passes to its eldest living member), and the heir's row becomes the hero's row (`Hero`, `PersonIndex` moves to it) after Phase 4's `Depart`. Estates, deeds and the settle are P5-H.
- **v7 saves** (the one v8 step): the step marks the hero section's `PersonIndex` pending (it cannot insert a row: a section step has no regenerated people); the load inserts the row and household from the Phase 4 kin links the save holds, rolling nothing (`InsertForLoad`), so the world loads and saves as a v8 world. Tested on a real v7 save captured with the Phase 4 code (`Save/Data/v7_fight_in_progress.rgsave`). `InheritanceRecord.DeedsPending` is `Deeds` (same save key, empty in every v7 save). A real v7 save from a Phase 4 build is still refused by the game's content-hash check (the content changed): that rule is older than this step and unchanged; the test passes the save's own hash.

The pack (the overview's A4 line; crafting-full CF-4):
- The pack already was the economy's: a person store and item rows owned by the hero's id, saved in the economy section (Phase 3 trade). What did not read it: the fight kit chose its weapon from the record's starting kit, the crafting screen and the desk took tools from it too. Now `FightSetup.HeroKit` takes `carried` (`HeroPack.Things`: the item rows with their material and quality, and counted ammunition), which the street fight passes from the hero view, and its worn pieces carry their rows' tier; `HeroDesk.CraftStart` adds the craft tools the pack really holds (`HeroContext.PackTools`) to the site whatever the screen sent, and the screen lists them from the view's rows and goods. A tool used at the barrier wears (items 4.3).
- Loot already went into the pack (Phase 4 `SiteDesk`); the reopened test checks it on a new-game world with the hero's row: the haul is item rows owned by the row's id, in the save, and the hero fights with the looted longsword and crafts with the looted chisel.
- CF-4: a torch made from the pack with a dagger from the pack as its knife, saved mid-step: the job's components are in its store in the economy section, the job in the crafting section, save -> load -> save byte-identical, and the loaded world finishes the same torch.
- The record's `Inventory` (the origin kit) stays in the hero section and only seeds the pack at a new game.

Still open for P5-H and later: estate lists, `EstateSettler`, wills, the will panel, the succession screen's "You inherit" list (the character screen shows the family now: `in_family`, `in_kin_*`); per-culture `inheritance_law`.
