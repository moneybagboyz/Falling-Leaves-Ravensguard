# Reputation and standing: what groups think of a person, renown, marks, and reputation that passes to the heir

Plan refs: §3.10 (factions react to you; reputation), §3.7 (an Ironman heir inherits property **and reputation**), §5 Phase 4 ("reputation in Phase 6"), §3.4 (rumours spread person to person, so "you overheard it" is real), locked decisions (slavery and faith carry heavy reputation consequences). Phase 3 dialogue Q1: slander arrives with reputation (Rumours v2, `contracts-and-rumours-v2.md`).

## 1. Purpose
One table answers "what does this group think of this person, and why". Phase 3 keeps per-pair trust (`RelationTable`) and spreads rumours from the chronicle; Phase 4 and 5 left `InheritanceRecord.ReputationPending` as an empty string. Phase 6 adds **standing**: a person's reputation with a group (a settlement, a fief, a realm, a religion, a guild, a band), computed from the deeds the group's people have actually **heard of** through rumours, plus **renown** (how widely known) and **marks** (a brand, an outlaw's name, excommunication). Prices, hiring, courts, petitions, marriages, councils and the church all read it. It is built early because crime, family, faith, rule and diplomacy all write to it.

## 2. Dependencies
| Needs | From |
|---|---|
| `Chronicle` entries with subjects and causes; `RumourTable`, hearings per person, `TalkSystem` | Phase 0, Phase 3 |
| `RelationTable` (per-pair trust and familiarity, flags) | Phase 3 |
| Settlements, fiefs, realms, guilds (Phase 5), religions (`faith-and-church.md`) | exists / this phase |
| `InheritanceRecord.ReputationPending`, `SuccessionDesk` | Phase 4-5 |
New: `Sim/Society/StandingBook`, `Sim/Society/DeedTable` (content `society/deeds.jsonc`), `Sim/Society/Marks`. Extended: `RumourTable` (hearing counts per group, kept incrementally), `SuccessionDesk` (inherits standing), the dialogue vocabulary (`has_standing_at_least`, `has_mark`).

## 3. Data
- **Groups** (`GroupRef`, 4 B: kind + index): `Settlement`, `Fief`, `Realm`, `Religion`, `Guild`, `Band` (a bandit or slaver band, a mercenary company), `Household` (only for the hero's own and noble houses). A group's members are the persons whose home, fief, realm, faith, guild or band it is.
- **`DeedRow`** (content `society/deeds.jsonc`, about 60 rows): a chronicle type (optionally narrowed by data, as rumour types are) -> deltas in standing permille per group kind relative to the doer, e.g. `theft_witnessed` { settlement of the victim -60, fief -30 }, `murder` { settlement -300, fief -150, realm -50, religion -40 }, `slave_bought` { settlement -40 where the culture's law is `none`, +0 where `chattel` }, `slave_freed` { religion +60, settlement +30 }, `alms_given` per 100 mites { settlement +5, religion +3 }, `pilgrimage_done` { religion +80 }, `monster_slain` { settlement +200, region-wide renown +60 }, `bounty_collected` { fief +40 }, `riot_led` { fief -200, settlement +80 among the aggrieved }, `sentence_served` { +half of the crime's delta back }.
- **`Standing`** (saved, sparse, optional section `standing`): `(person, group) -> { ValuePm (-1000..1000), Heard (count of members who heard at least one counted deed), LastDay }`. Only persons with a counted deed have rows (the hero, his party and kin, notables, outlaws): about 2,000 rows a region.
- **`Renown`** per person (0..1000): the share of the region's adults who have heard any deed of his, x1000, decaying 5% a season after a year without a new deed.
- **Marks** (bit flags per person, saved): `Branded(thief|murderer|runaway)`, `Outlaw(realm)`, `Excommunicated(religion)`, `Enslaved`, `Pardoned`. Visible marks (a brand on the hand or cheek) are seen at a first meeting within 3 m in light.

## 4. Rules

### 4.1 Standing from heard deeds
A deed is a chronicle entry whose subject is the doer and whose type has a `DeedRow`. Its effect on a group grows as the group's members hear the rumour: `contribution(d, g) = delta(d, g) x heard(d, g) / members(g)`, with `heard(d, g)` counted incrementally when a hearing is added (Phase 3 `RumourTable`), so a theft seen by two people in a town of 1,400 moves the town's standing by `-60 x 2/1,400` at first and by up to -60 when everyone knows. A deed nobody witnessed and nobody tells has no effect (crimes only matter when witnessed, §3.10). **Age:** a contribution counts fully for a game year, then 10% less each further year, never below 30% (people remember a murder). `ValuePm = clamp(sum of contributions, -1000, 1000)`, recomputed for the rows touched that day at H5.

### 4.2 What reads standing (one function, `Standing.Of(person, group)`)
| Reader | Effect (linear, clamped) |
|---|---|
| Market prices (Phase 2 book) for the person | +-10% at -+1000 with the settlement (the trader's own trust from `RelationTable` adds as today) |
| Hiring, recruiting | refused below -300 with the settlement; recruits' starting loyalty + standing/20 |
| Guards | a person below -500 with the fief is watched (followed 20 m) in town; an outlaw is attacked on sight |
| Courts | mercy step (`crime-and-justice.md` 4.6): standing with the fief >= 300 lowers the sentence one step |
| Petitions, councils, marriages, fealty | the utility terms in `rule-and-income.md`, `diplomacy-and-ai-lords.md`, `family-and-lifespan.md` read standing / 10 |
| The church | weddings, burial, absolution refused below -500 with the religion; excommunication at -800 if sins continue (`faith-and-church.md`) |
| Dialogue | `has_standing_at_least`, barks ("You're the one who freed the thralls at Oxley") |

### 4.3 Marks
A brand is applied by a sentence (`crime-and-justice.md`) as a burn wound and a mark; it never fades. A stranger's first-meeting trust starts at -200 pm for a thief's brand, -400 for a murderer's. Outlawry: anyone in that realm may kill the outlaw without a crime; merchants who recognise him refuse to trade; his standing with the realm is set to -1000 for as long as the mark lasts. A pardon (bought, granted, or a ruler's act) clears `Outlaw` and adds `Pardoned`.

### 4.4 Inheritance of reputation (closes the Phase 4-5 pending item)
When play passes to an heir who is **kin**, the heir's rows start at **50%** of the dead hero's standing with every group (the family name), plus his own; renown 50%. A companion heir (not kin) inherits nothing of it. Marks never pass. `InheritanceRecord.ReputationPending` becomes the list of groups carried (same save key, now meaningful; empty in every v8 save).

### 4.5 Tick placement
| Where | What |
|---|---|
| Hearing added (H7 rumour step, barrier for the hero's talk) | `heard(d, g)` +1 |
| H5 hour 0 | recompute touched rows, renown decay at a season's first day |
| Barrier | marks from sentences, pardons; succession |

## 5. Determinism
No random draws: standing is a sum over heard deeds in ascending (entry id, group) order, integer permille. Stream **0x620** is reserved for slander (`contracts-and-rumours-v2.md`); this spec draws nothing. `StandingHash` covers rows and marks.

## 6. UI (960 x 540)
Character screen (C) > **Standing** tab: groups with a value, a band label (Hated, Distrusted, Unknown, Known, Respected, Honoured) and **why**: the top three deeds by contribution, each a link to its chronicle entry (H). The Faction screen (F) shows standing with each realm and fief. Marks shown on the paper-doll and in the name line.

## 7. Tests (golden)
| id | Case | Expected |
|---|---|---|
| RP-1 | A theft seen by 2 in a town of 1,400 | day 0: -60 x 2/1,400 = 0 pm (integer); after rumours reach 700: -30; never past -60 |
| RP-2 | Unwitnessed theft | no deed row, no standing change, no rumour |
| RP-3 | Murder known to everyone in the village, 3 years on | -300 x 80% = -240 with the village; at 9 years the floor -90 |
| RP-4 | Price effect | standing +1000 with the settlement: the hero pays 10% less than at 0 for the same lot of bread |
| RP-5 | Brand | a stranger meeting the branded hero starts at -200 pm trust; a mark survives save and load |
| RP-6 | Outlaw | a guard attacks on sight; killing the outlaw raises no crime; a pardon clears it |
| RP-7 | Heir of kin | the son starts at 50% of every standing row and renown; a companion heir at 0; marks not passed |
| RP-8 | Renown | a monster slain and heard by 60% of the region's adults: renown 600; decays 5% a season after a year with no deeds |
| RP-9 | Group membership changes | a household moving to another town takes its hearings with it: the old town's `heard` -1 for each deed it had heard, the new +1 |
| RP-10 | Determinism and save | 30 days of a soak with the hero bot: `StandingHash` equal across two runs and after save -> load -> save |

## 8. Open questions (recommended defaults)
1. **How much of a dead hero's name passes to kin:** 50% (renown too). Default as written.
2. **Do NPCs get standing rows?** Only those with a counted deed (notables, criminals, the hero's kin); everyone else reads 0. Default as written.

## 9. Budgets
A hearing's increment O(groups of the hearer) <= 0.5 us; H5 recompute <= 1 ms a region (about 2,000 rows); save <= 60 KB (2,000 rows x 24 B, marks 2 B per marked person).
