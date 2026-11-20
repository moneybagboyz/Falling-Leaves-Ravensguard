# Crime and justice: witnesses, the hue and cry, bounties, guards, jail, courts and historically harsh punishments

Plan refs: §3.10 (owner flags; crimes only when witnessed, the HUD shows "seen" or "unseen" before you act; bounties, guards, jail; reputation), §3.7 (guards jail you; ways out: pick the lock, work off the sentence, be ransomed, rescued, escape in transfer), §5 Phase 6 (historically harsh punishments: fines, the stocks, flogging, branding, outlawry, a hand cut off for repeat theft, hanging for murder or treason; maiming through the real anatomy and wound system; the sentence follows the local lord's and culture's law; the chronicle records the cause), §3j (justice: fines and forfeitures), §3b (barrow robbing is a crime and a taboo), Phase 4 open items (guards fine and release: a stub; the captors' camp; prisoners are not fed).

## 1. Purpose
Make law real people doing real things: a witness who saw it, a shout, a guard who comes, a bounty with escrowed coin, a cell, a court day, a lord who judges by his culture's table and his own temper, and a sentence carried out on the body through the Phase 4 wound system. Every step is a chronicle entry with its cause, so "why is there a price on my head?" always has an answer. **Done-when (plan):** theft in front of a witness raises a bounty with an explained cause.

## 2. Dependencies
| Needs | From |
|---|---|
| `ActionTag.Crime` (autopilot `NoCrimes`), owner flags on items, stores, rooms, deeds | Phase 3, 5 |
| `LightGrid`, `LevelGrid` line of sight, `Stealth` and `Perception` | Phase 1, 3 |
| `Captivity` (`CaptiveBook`, `Restraint`, `EscapeWindow` 0x454, `LockPick` 0x455), `DefeatTable` (`WinnerKind.Guards`), watch mode `NoLethalForce` (subdue) | Phase 4 |
| `WoundBook`, `Treatment.Perform(Amputate)`, burn wounds | Phase 4 |
| Culture `crimes` tables (keys such as `fine_or_hand`, `stocks_then_hand`, `weregild_or_outlawry`) | content, Phase 1 |
| Standing and marks | `reputation-and-standing.md` |
| Bounties as contracts with escrow | `contracts-and-rumours-v2.md` |
| Fief treasuries, lords, `Occupation.Guard`, the manor | Phase 2 |
New: `Sim/Law/` (`CrimeBook`, `Witnesses`, `HueAndCry`, `Bounties`, `Gaol`, `Court`, `Sentences`, `LawContent`), content `law/crimes.jsonc`, `law/punishments.jsonc`. Extended: `CaptiveBook` (`CaptorKind.Gaol`, appended), `DefeatTable` (guards arrest instead of fine and release), `UtilityBrain` (`Pursue`, `Report` actions), the guard schedule (day and night shifts).

## 3. Data
- **`CrimeKind`** (content ids, about 20): theft, burglary, pickpocketing, poaching, assault, murder, arson, trespass, smuggling, unlicensed_mining, grave_robbing (taboo), sacrilege, heresy, treason, debt_default, embezzlement (officials), bribery, harbouring (a runaway or outlaw), slaving (where forbidden), jailbreak, squatting (civil: eviction, `holdings-and-works-v2.md`).
- **`CrimeRecord`** (saved, optional section `law`): `{ Id, Kind, Perpetrator (person or -1 unknown), Description (clothing colours and build when unrecognised), Victim (person, household, fief, church), Place, Day, Witnesses[] (person ids with Recognised bit), ValueMites, Entry (chronicle id), State: Seen -> Reported -> Accused -> Held -> Tried -> Sentenced -> Served | Fled | Pardoned }`, plus `Convictions[(person, kind)]` counts for escalation.
- **`law/punishments.jsonc`**: each culture key maps to an escalation by conviction count, e.g. `fine_or_hand`: [fine 2x value + 100, fine 4x value + 300, hand], `stocks_then_hand`: [stocks 1 day, flogging 20, hand], `weregild_or_outlawry`: [weregild 6,000 to kin, outlawry if unpaid in 15 days], `hanging`: [hanging], `drawn_and_quartered`: [hanging, the body displayed 15 days], `restitution_sevenfold`, `blood_price`, `blood_feud_or_price` (the victim's kin may kill the culprit without a crime for a year unless the price is paid), `bond_labour` (days = debt / 150 mites), `gaol` (days = debt / 100, or 15 days), `penance` (church), `exile` (leave the realm in 3 days or be outlawed), `confiscation_and_fine`, `kin_pays`, `outlawry`, `flogging`, `branding`.
- **Punishments as body effects** (content, applied through Phase 4): `flogging N`: N/10 skin cuts on the back parts, grade 1 each, pain 25 each, capillary bleeding (clots), infection rules as any wound; `branding`: a burn wound grade 2 on the hand (thief) or cheek (murderer, runaway) and a mark; `hand`: amputation at the wrist by the executioner (`Treatment.Perform(Amputate)` with skill 40, cauterised stump: Phase 4 4.6), the right hand unless the culture says left, recorded in `Permanent` with the court as cause; `stocks`: 1 game day held in the market square, cannot move, pain +5 an hour from pelting, standing -20 with the settlement; `hanging`: death (`DeathCause.Executed`, appended) at dawn after the court day.
- **Fees and reasons** (mites): fines to the fief treasury (`TransferReason.Fine`, exists), restitution and weregild to the victim's household (`Gift` renamed in views as Restitution: a new `TransferReason.Restitution`, appended), bounties from escrow (`TransferReason.Bounty`, appended), bribes (`TransferReason.Bribe`, appended).

## 4. Rules

### 4.1 Seen or unseen (deterministic, shown before acting)
Witnessing has no dice, so the HUD can be honest. A person **sees** an act when he is awake, alive, within his **sight range** and has line of sight to the actor's cell (walls and closed doors block; `LevelGrid` portals). Sight range: daylight outdoors 30 m, dusk 15 m, night 6 m, plus 6 m where the actor's cell is lit (a torch, a fire, a lit window: `LightGrid` level >= 3); indoors 12 m lit, 4 m dark; heavy rain x0.7 (Phase 4 weather row). Sneaking shrinks every observer's range to the actor by `Stealth / 2`% (Stealth 60: x0.7). The HUD's eye icon shows **Seen (n)** when n observers would see the act now, else **Unseen**, updated at 5 Hz while an act tagged `Crime` is targeted. A witness **recognises** the actor when familiarity >= 1, or the distance is <= 10 m in light, or the actor bears a visible mark; otherwise he remembers the description (the outer layer's colours and build), matched later against the person's current clothes (a disguise works).

### 4.2 After the act: the hue and cry, reports
- A witness of a theft, assault, murder or arson in a settlement **raises the hue and cry** when he does not fear the actor (actor's renown < 600 or the witness is with 2+ others) and is not his kin or friend (trust < 500 pm): every person within 30 m hears it (walls x0.5); guards within 80 m switch to **Pursue**; townsfolk with Melee >= 30 and no fear join (at most 3). An actor caught (surrenders or is subdued with `NoLethalForce`) is **Held**.
- A witness who does not raise the cry **reports** at his next contact with a guard, the reeve or the lord's household, at the latest at the next H5 (abstract) with P = 90% (stream **0x610**, key crime id and witness, draw 0), less 40 points if he fears the actor and 60 if his trust in him is >= 500 pm. A report makes the crime **Reported** and the chronicle `crime_reported` (cause: the `crime_witnessed` entry, whose cause is the act).

### 4.3 Bounties
On a report with a recognised perpetrator (or a description), the lord of the place (`LandRegistry.LordOf`) posts a **bounty**: amount by kind (content): theft and burglary 2 x value + 100 mites, assault 500, murder 3,000, arson 2,000 + damage/10, treason 10,000, a runaway slave the owner's choice (default 30% of the price), capped at 25% of the fief treasury; escrowed from the treasury into the contract (`contracts-and-rumours-v2.md`, kind `bounty`); alive pays all, dead pays half (all for an outlaw or a murderer); expiry 60 game days, an outlaw's never. The chronicle entry `bounty_posted` has causes `crime_reported` -> `crime_witnessed` -> the act, and names the witness. Bounty hunters (NPC guards, mercenaries, the hero) take it like any contract.

### 4.4 Guards
Guards (`Occupation.Guard`; about 1 per 60 people in a town, 1 per manor in villages) work shifts: day 06-18 and night 18-06 with half the men; night guards patrol fixed loops (content per settlement plan: the square, the gate, the manor), the rest sleep in shifts (25% asleep at any night hour after 02:00). A guard who sees a crime or hears the cry pursues at a run, calls "Yield!" (an outlaw or murderer: no call), and subdues (Phase 4 watch `subdue`: blunt blows, no lethal force) unless the culprit draws steel, then fights normally. A defeat by guards is an **arrest** (`DefeatTable`: `WinnerKind.Guards` -> `Captured` with `CaptorKind.Gaol`), replacing Phase 4's fine and release.

### 4.5 Gaol (captivity completed)
The held go to the fief's gaol (a `gaol_cell` room in the manor cellar or the town gaol: room type appended; else the manor's locked storeroom) as captives (`CaptiveBook`) with `Restraint.Bound` (`Chained` for murder and treason). **Prisoners are fed** 1 measure of bread a day from the fief's stores or bought with the treasury (closes the Phase 4 open item; the same rule feeds bandits' captives from the band's stash, and the captors' camp becomes a real camp site in the live area, `heists-and-jailbreaks.md` 4.1). Ways out (Phase 4 list completed): serve the sentence, pay the fine, **bribe the gaoler** (one rule for every official, `heists-and-jailbreaks.md` 4.3: an offer of at least `3 x daily wage x (60 - honesty) / 20` is accepted with P = `(60 - honesty)` %, honesty from `rule-and-income.md` 4.8; stream 0x613, key official and day; an honest official (>= 60) never takes one; a refused bribe is a new crime), pick the lock (0x455, existing), be rescued (`heists-and-jailbreaks.md`), escape in transfer (0x454), or be ransomed by a lord for a captive knight (Phase 4).

### 4.6 The court and the sentence
- **Court day:** the last day of each five-day month (culture calendar) at the fief's manor (the lord, else his steward or bailiff presides), and the market day in a chartered town (the mayor). The held and the accused who come are tried in crime id order.
- **Proof:** guilty when at least one living witness who recognised the accused attends (witnesses are told to come; a witness 15+ km away or dead does not), or the accused is caught in the act, or confesses. Otherwise acquitted (`crime_acquitted`).
- **The sentence** is the culture's key for the crime (or the **lord's law**: a lord may replace any culture key with another from the culture's allowed list, set in the fief's law panel; NPC lords choose by trait: harsh, just, merciful), at the step of the convicted's count of that crime. **Mercy:** the presiding lord's mood shifts it by one step: down when the accused's standing with the fief >= 300, or he pays full restitution before sentence, or claims **benefit of clergy** (Literacy >= 30 in a culture with church courts: the case goes to the bishop and the sentence becomes penance); up when the lord's opinion of him <= -50; a draw decides ties for NPC lords (stream **0x612**, key crime id, P 20% either way). The hero sees the expected sentence before the court day in the cell ("Theft, second conviction: Valeholt law: flogging, 20 lashes").
- **Carrying it out** the next morning in the square, in public (witnesses: rumours, standing). Maiming and branding are real wounds (data 3); a lost hand stays lost (`Permanent`), and the cause chain reads "hand cut off by the court of Oakford for theft (second conviction), day 214 <- theft of a knife from Wynn's stall, day 201, seen by Wynn".
- **Outlawry and forfeiture:** an outlaw's deeds in that realm are forfeited to their lords (`DeedCause.Forfeited`), his standing set (reputation 4.3), the bounty permanent.

### 4.7 The hero as judge
A lord (or his steward) presides on court days in his fief: the **Court** panel lists the cases (accused, crime, witnesses, the culture's sentence and the allowed alternatives). Sentences move unrest (`unrest-and-revolt.md`: a sentence harsher than the culture's key +1 grievance for the convicted's kin households; acquitting the plainly guilty +1 for the victim's) and the lord's standing (harsh: fear; just: respect). Delegated (the steward judges) when the hero is away.

### 4.8 NPC crime (so the law has work)
Desperate households (hunger or arrears) steal: P = 0.5% a day per desperate household (stream **0x614**, key household and day), value up to 300 mites from a neighbour or a stall; witnessed per the same rule in the abstract (a witness exists with P = 40% by day, 15% at night). Unguarded construction piles lose 0.2% of their value a night in towns (theft from site piles, Phase 5 T4). Bandits, smugglers and poachers come from their own systems. Expected volume: about 25 thefts and 3 assaults a season in a town of 1,500, about 60% reported.

### 4.9 Tick placement
| Where | What |
|---|---|
| Live tick (5 Hz) | seen/unseen for a targeted act; witness set at the act's tick; hue and cry; pursuit |
| Barrier | arrests, bribes, the court day's trials and sentences (the hero's case and those he judges) |
| H4 slice | NPC crime rolls, reports by witnesses who met no guard |
| H5 hour 0 | prisoners fed; sentences served a day; bounty expiry |
| Court day (H5 of the month's last day) | NPC trials in crime id order |

## 5. Determinism
Seeing and recognising: no draw. Draws: **0x610** reporting (key crime, witness), **0x611** guard recognition of a described person by clothes (key guard, person, day: P 70% when every described colour matches), **0x612** NPC lord's mercy tie, **0x613** bribes, **0x614** NPC crime. Ordering by crime id, witnesses by person id. `LawHash` covers crimes, convictions, bounties and gaol rows.

## 6. UI (960 x 540)
The HUD eye (Seen n / Unseen) beside the stance; the Journal's **Wanted** list (bounties on the hero with the cause chain); a cell view (sentence expected, ways out, days served); the **Court** panel (context `Court`, overview 6); the paper-doll shows brands and the stump; the fief's **Law** panel in the Ledger (the lord's law per crime). Barks: "Stop, thief!", "In the lord's name, yield!".

## 7. Tests (golden)
| id | Case | Expected |
|---|---|---|
| CJ-1 | **Done-when.** Seed 1234, day 3, 10:00, Ranunfield market: the hero takes a 240-mite knife from Wynn's stall, Wynn at 2 m, 4 others at 8-25 m, daylight | HUD showed Seen (5) before; 5 witnesses, Wynn recognises (2 m, light); the cry raised; if the hero escapes the guards: by the next H5 `crime_reported`, a bounty of **580 mites** escrowed from the fief treasury; `bounty_posted`'s cause chain resolves `crime_reported` -> `crime_witnessed` (Wynn) -> `theft`; standing with Ranunfield moves by RP-1's rule |
| CJ-2 | Same at 23:00 with no light, sneaking, Stealth 60, nearest person 9 m | night range 6 m x0.7 = 4.2 m: Unseen; no crime record, no rumour |
| CJ-3 | Unrecognised in light at 15 m, then changes cloak | described; a guard matches the old cloak with 0x611 (70%), never the new one |
| CJ-4 | Hue and cry | guards within 80 m pursue, those at 90 m do not; a kin witness raises nothing |
| CJ-5 | Arrest | defeated by guards: captive in the manor gaol, bound, fed 1 measure a day from the fief stores; Phase 4's fine-and-release path gone |
| CJ-6 | Court day, Valeholt, first theft | fine `stocks_then_hand` step 1: a day in the stocks; second: flogging 20 (2 back cuts, grade 1); third: right hand amputated, stump cauterised, `Permanent` cause chain names court, crime and witness |
| CJ-7 | Northmark `weregild_or_outlawry` | weregild 6,000 to the victim's kin; unpaid after 15 days: outlawry, deeds forfeited, bounty permanent |
| CJ-8 | Mercy | standing 300 with the fief lowers the step; benefit of clergy turns a Valeholt theft into penance |
| CJ-9 | No witness at court | the only witness died: acquitted, `crime_acquitted` |
| CJ-10 | Bribe | gaoler honesty 20, wage 150: an offer of 900 is accepted with P 40% (0x613), 600 is refused; a refused bribe adds a bribery charge |
| CJ-11 | Hanging | a convicted murderer (Valeholt) hangs at dawn: `DeathCause.Executed`; for an Ironman hero the heir takes over; a jailbreak before dawn prevents it |
| CJ-12 | The lord's law | the hero as lord replaces `stocks_then_hand` with `fine` for theft: NPC thieves are fined; kin grievances lower (UR-4) |
| CJ-13 | NPC crime volume, 1 season, town of 1,500 | 20-30 thefts, 2-5 assaults, 55-65% reported; every reported crime's chronicle entry has a specific cause |
| CJ-14 | Hero as judge | the Court panel lists cases with the culture's sentence and alternatives; the chosen sentence is carried out; unrest and standing move as 4.7 |
| CJ-15 | Grave robbing | a barrow item taken while seen: `grave_robbing` (taboo) crime, standing with the religion -100, the Phase 4 `TabooRobbed` rumour kept |
| CJ-16 | Prisoner of bandits fed | the band feeds captives from its stash; with an empty stash they are released after 2 days (no starvation in a cell) |
| CJ-17 | Determinism, save | a 30-day town run with NPC crime: `LawHash` equal across runs and after save -> load -> save |

## 8. Open questions (recommended defaults)
1. **Harsh sentences on the hero** (overview Q2): the law applies as to anyone; the expected sentence is shown in the cell before the court day, with every way out (fine, restitution, mercy, clergy, bribe, escape, rescue).
2. **Weregild amount:** 6,000 mites (about two game years of a labourer's wage net). Default as written.
3. **Description matching:** colours of the outer layer only (cloak, tabard). Default as written; a hood that hides the face makes recognition need 5 m.

## 9. Budgets
Seen/unseen at 5 Hz <= 0.1 ms (people within 40 m, LOS cached per 5 Hz step); witness set at an act <= 0.2 ms; hue and cry <= 0.1 ms; NPC crime <= 0.5 ms an H4 slice a region; a court day <= 2 ms a fief; save: crimes 48 B each, about 300 live a region (<= 30 KB with convictions and bounties).
