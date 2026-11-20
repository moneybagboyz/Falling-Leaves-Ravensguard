# Slavery: bondage by culture's law, slavers and slave markets, owning and freeing, being enslaved, escapes and revolts

Plan refs: locked decision (Nov 2026: **fully systemic, like Kenshi**: slavers and slave markets exist where the culture's law allows them, and you can be enslaved and escape; the player can buy, sell, own or free slaves, with heavy consequences for reputation, faith and law), §3.7 (slavers capture you), §3.10 (jail, slavery, reputation), Phase 4 morale-capture-succession C6 (a "sold" captive is released stripped with a rumour: the stub this replaces), Phase 5 inheritance 4.5 (an enslaved heir is skipped; a person is never an estate item in Phase 5).

## 1. Purpose
Bondage as a law of each culture, run by real people: slavers who raid and buy captives, pens and markets in the towns whose law permits it, owners who feed and work their slaves, slaves who flee, are hunted, buy their freedom or rise. The player meets it from every side: he can be taken and sold, he can buy, own, sell or free, and every choice moves standing, piety and the law. Nothing is abstracted into a number: a slave is a person row with a bond.

## 2. Dependencies
| Needs | From |
|---|---|
| Persons, households (servants), employers, needs baskets, `LabourMarket` | Phase 2, 5 |
| `CaptiveBook`, `Restraint`, escapes (0x454), lockpicking (0x455), bandit styles (`kidnapper` 15%), the band's stash | Phase 4 |
| Crime, bounties, gaol, sentences (`bond_labour`), standing and marks | `crime-and-justice.md`, `reputation-and-standing.md` |
| Religions' `slavery_stance` | `faith-and-church.md` |
| Contracts (runaway bounties, slave hunts) | `contracts-and-rumours-v2.md` |
| The riot machinery | `unrest-and-revolt.md` |
| Estates (a slave is passed as a bond, not an item) | `EstateSettler` (Phase 5) |
New: `Sim/Bondage/` (`BondBook`, `SlaverBands`, `SlaveMarket`, `BondWork`, `Runaways`), content `society/slavery.jsonc`, culture field `slavery`.

## 3. Data
- **Culture law** (`cultures.jsonc`, new field `slavery`): `none` (no slaves; owning or trading is the crime `slaving`), `debt` (bond for debt or a sentence, for a term), `war_captives` (thralls taken in war or raids; children free), `chattel` (bought and sold, children follow the mother). Base content: Northmark `war_captives`, Valeholt `none`, Highmarch `debt`, Seaboard `chattel` (port slave markets). A lord may not exceed his culture's law; a realm's ruler may forbid (an act, chronicle entry).
- **`Bond`** (saved, optional section `bondage`): `{ Person, Owner (person, household, business or fief), Kind (Debt, Thrall, Chattel, Convict), Since, TermEndDay (Debt and Convict; 0 = for life), PriceMites (last paid), Origin (Captured, Sold, Sentenced, Born, Debt), Peculium (coin the slave holds by the owner's leave), Treatment (Harsh, Fair, Kind: the owner's setting), Chain (Restraint) }`. A bonded person stays `PersonStatus.Alive`: he eats, sleeps, ages and can be wounded as anyone; he takes no wage and cannot leave, marry or own without the owner's leave.
- **`society/slavery.jsonc`**: price table (mites) for an adult of 16-40 in health: **9,000** (about 90 days of an unskilled wage, net of keep), x1.5 with a trade skill >= 40, x2 a master (>= 70), child under 12 x0.4, 12-15 x0.7, over 50 x0.4, maimed or a lost limb x0.5, infirm x0.2; raid rates; revolt thresholds; standing and piety deltas (also rows of `society/deeds.jsonc`).
- **Slaver bands**: a `Band` group (bandit-like faction, content archetype `slavers`) with a camp (a site), 6-15 people, a pen; they sell at markets of `chattel` towns and to `war_captives` lords. Spawned per region by the bandit frequency slider (`difficulty-sliders.md`): 1 band per 2 regions at 100%.
- **Markets:** a `slave_market` lot (a pen with a block) in each town of a `chattel` culture (Seaboard ports) and at fairs of `war_captives` towns. A market's stock is the pen's persons with asking prices.

## 4. Rules

### 4.1 How people become bonded
1. **Captured and sold:** a band of style `kidnapper` (Phase 4) or a slaver band holds captives 10 days, then marches them to the nearest market of a culture that accepts them (Phase 4's 20% "sold" stub becomes this). A slaver raid on a village takes 2-8 people (P per region per season = 15% x bandit slider, stream **0x630**, key region and season) where the village's lord is weak (guards < 3).
2. **War captives** (abstract battles v1 now, Phase 7 armies later): commoners captured by an army of a `war_captives` or `chattel` realm become thralls of the victor's lord.
3. **Debt and sentence:** a `bond_labour` sentence or a debt default in a `debt` culture binds the debtor to the creditor for `debt / 150` days (the day labour wage); a `Convict` bond for a sentence term to the fief's works or mine.
4. **Birth:** in `chattel` law a child of a bonded mother is bonded to her owner.
The hero is enslaved by the same rules (a defeat by slavers: `DefeatTable` `Captured` with `CaptorKind.Slavers`, appended).

### 4.2 Buying, owning, selling, freeing
- **Buy** at a market or from a band (Q > Trade > the pen's list): the price to the seller's purse (`TransferReason.SlaveSale`, appended); the bond's owner becomes the buyer; the slave walks with him (`Restraint.Bound` unless the owner sets none). NPC buyers: households of `chattel` culture with coin >= 3 x price and a free bed, businesses short of staff (one buy a business a season).
- **Own:** the slave is a member of the owner's household (as a servant), assigned work like staff (Phase 5 staff without wage) or follows the hero (overview Q3: a party member at follower morale, flees below 40 unless chained; fights only if armed by the owner). The owner feeds him: needs basket at 100% (Kind, Fair) or 70% (Harsh: health and morale fall; a hungry slave counts toward a revolt).
- **Sell:** at a market or to a buyer (Q > Sell bond).
- **Free (manumission):** Q > Free, or by a will; a chronicle entry `slave_freed`; standing and piety deltas (`deeds.jsonc`); a freed person becomes a free member of the household or leaves (by his trust in the owner >= 0: stays as a paid servant).
- **Self-purchase:** a slave whose peculium reaches his last price buys himself free (the owner must accept in `debt` law, may refuse in `chattel`).

### 4.3 Law, faith and standing
- In a `none` culture: buying, owning or selling is the crime `slaving` (fine 2 x price and the slave freed; a second conviction outlawry), witnessed like any crime; freeing is lawful anywhere.
- Faith (`faith-and-church.md`): a religion with `slavery_stance: forbid` counts owning a slave as a sin (piety -10 a season while owning; -500 standing with the religion after a year: excommunication risk); `coreligionists_forbidden` counts only slaves of the owner's own faith; `accept` none.
- Standing (`deeds.jsonc`): buying -40 with the settlement where the law is `none` or `debt`, 0 where `chattel`; selling a person of the settlement's own culture -150 with it; freeing +30 with the settlement, +60 with the religion; enslaving the hero leaves a mark (`Enslaved`) visible as shackle scars only if chained for 30+ days.

### 4.4 Being enslaved (the hero) and escaping
The enslaved hero keeps control (Kenshi style): he works where his owner sets him (fields, a mine, a galley's bench in Phase 7), in chains or not, fed per Treatment; play continues. Ways out: escape (pick the chains with 0x455; at night when the overseer sleeps; in transfer, 0x454), buy himself free (peculium from tips and odd jobs allowed by a Fair or Kind owner: 2-10 mites a day), be bought free by a companion or kin, be freed by a revolt or a raid, or win the owner's trust (>= 700 pm and a year: freed). A runaway in a slave-holding realm carries the bond: the owner posts a runaway bounty (crime 4.3), slave catchers take it; a runaway who reaches a `none` realm is free there (its courts do not return slaves).

### 4.5 Revolts
On any holding with >= 10 bonded persons, at H5: grievance share = bonded who are hungry, Harsh-treated, flogged in the last season or separated from kin by a sale, / all bonded. When the share > 50%, guards (free armed adults) / bonded < 1/5, and a bonded person with `Leadership >= 40` lives there, a revolt starts with P/day = `(share - 50) / 10` permille (stream **0x633**, key holding and day). It uses the riot machinery (`unrest-and-revolt.md` 4.3) with the bonded as rioters: they break chains (Restraint off), take tools as weapons, fight the guards (live in the bubble, abstract battle v1 outside), and the survivors flee as a free band (a bandit group with the leader) or hold the place. Chronicle `slave_revolt` with its causes (the hunger, the floggings by name).

### 4.6 Tick placement
| Where | What |
|---|---|
| Barrier | trades, manumissions, the hero's escapes |
| H4 slice | feeding by Treatment, work as staff, peculium |
| H5 hour 0 | runaway checks (a bonded person flees with P from morale and chains, stream **0x632**), revolt checks, term ends |
| H6 S7 | slaver raids, band marches to markets (with migration) |
| S5 | market restock (pens filled from captives held 10+ days) |

## 5. Determinism
Streams: **0x630** raids, **0x631** market buyers' choice ties (key market and day), **0x632** runaways, **0x633** revolts. Buyers are taken in (coin desc, household id) order. `BondHash` covers bonds and pens.

## 6. UI (960 x 540)
The pen's list on the trade screen (person, age, skills, health, origin, asking price); the Ledger **People** tab (owned bonds: work, Treatment, peculium, term, grievance); Q verbs Buy, Sell bond, Free, Set treatment, Chain or unchain. The enslaved hero's HUD shows the owner, the work, the chains, the peculium and known ways out. Every bond row links to its chronicle cause.

## 7. Tests (golden)
| id | Case | Expected |
|---|---|---|
| SL-1 | Prices | a 25-year-old smith of skill 50, healthy: 13,500 mites; a 13-year-old: 6,300; a one-handed adult: 4,500 |
| SL-2 | Phase 4 stub replaced | a `kidnapper` band's captive is marched to the nearest Seaboard market after 10 days and listed; nobody is "released with a rumour" |
| SL-3 | Buying in a `chattel` town | coin moves seller -> buyer, the bond's owner changes, standing unchanged there; the same purchase carried into Valeholt is the crime `slaving` when seen |
| SL-4 | Debt bond | a Highmarch debtor owing 4,500 mites is bound 30 days, then free; term on the bond row |
| SL-5 | Manumission | standing +30 with the settlement, +60 with the religion; the freed man stays as a paid servant when trust >= 0 |
| SL-6 | Faith | owning one slave under a `forbid` religion: piety -10 a season; standing with the religion -500 after a year |
| SL-7 | Hero enslaved | defeat by slavers: captive, sold at a market, works chained in fields; escape by lockpick at night succeeds per 0x455; a runaway bounty is posted by the owner |
| SL-8 | Runaway reaches a `none` realm | no bounty can be collected there; the bond is void in that realm's courts |
| SL-9 | Self-purchase | a Fair owner, peculium 5 a day, price 9,000: free on day 1,800 (scaled test: price 500, day 100) |
| SL-10 | Revolt | a mine with 20 Harsh-treated bonded, 2 guards, a leader of 45: the revolt fires in the expected window (0x633), chains broken, a free band forms, chronicle causes name the floggings |
| SL-11 | Inheritance | an owner dies: bonds pass with the estate by law (as holdings, `EstateSettler`), a will may free them |
| SL-12 | Enslaved heir skipped | Phase 5 rule kept: a bonded kin is not a play heir |
| SL-13 | Raids and the slider | 100 seasons over 10 regions: raids within 15% +-3 per region-season at 100%, 30% +-4 at 200% |
| SL-14 | Determinism and save | `BondHash` equal across runs; save -> load -> save byte-identical with a pen, a runaway and a chained hero |

## 8. Open questions (recommended defaults)
1. **Slaves in the party** (overview Q3): they follow and work; fight only if armed; flee below morale 40 unless chained; a freed slave can be recruited as any companion.
2. **Base cultures' laws:** Northmark thralls, Valeholt none, Highmarch debt, Seaboard chattel. Default as written; mods add more.
3. **Children of slaves:** follow the mother only in `chattel` law. Default as written.

## 9. Budgets
Feeding and work per bonded person <= 1 us an H4 slice; runaway and revolt checks <= 0.3 ms a region at H5; a market restock <= 1 ms; save: bonds 32 B each (a few hundred a region where chattel is the law, <= 20 KB).

## Notes (P6-D1, slavery)
- **Built** as `Sim/Bondage/` (see `docs/reviews/phase-6-open.md`, "From step 4, part D1"): bonds as rows of a book (the person stays a person row), prices from the content table, buying and selling at pens through the ledger, debt and sentence bonds, manumission, self-purchase, treatment and chains, runaways with the lord's bounty, the law's eye on slaves in a realm that forbids them, inheritance, births, raids on weak villages, the pens filled from the Phase 4 holds, revolts' slave side, the faith's season, the hero's own bond and his ways out, the "bondage" section.
- **Rulings where the spec was silent:** the hero's person id is the value 0, so "nobody" is `Slavery.Nobody`; the grievance share is a whole percent and a revolt's daily chance is `share - 50` in ten thousand (5 per mille at 100%); a Harsh owner floggs once a season and writes one `slave_flogged` entry the revolt names; the peculium is a ledger purse of the slave's own, filled by the owner's daily tip (Fair 5 mites, Kind 8), so it is conserved and a self-purchase is a transfer; a chattel law lets a Harsh owner refuse his slave's purchase, a debt law does not; a slave carried into a realm whose law is none is freed by the lord's men on the day a resident sees him (50% a day, stream 0x631), twice the price in a fine to the fief's treasury as far as the owner's purse goes, a second time outlawry in the realm; four seasons of slaves kept against a doctrine make the church name it (`slave_held_year`, -500 with the religion).
- **Deviations:** wages, staff work, squad membership and the live street are not built (a slave is a household member and a flag); revolts end in a freed band and a record, not a fight; the pens are fed by Phase 4 holds and raids only; see the open-items file for the full list.
- **Tests** (`tests/Ravensguard.Sim.Tests/Bondage/`): `SlaveryTests` (SL-1 prices, SL-2 the stub replaced, SL-3 buying and the law's eye, SL-4 debt and sentence, SL-5 manumission, SL-8 runaway and the free town, SL-9 self-purchase, SL-10 revolt, SL-11 inheritance, SL-13 raids and the slider, SL-14 hash and section), `SlaveryWorldTests` (SL-6 the faith, SL-7 the enslaved hero from the defeat to the escape, SL-12 heirs, the desk, a raid day, the whole world's save), and a bondage step in `WorldRuntimeResumeTests`.
