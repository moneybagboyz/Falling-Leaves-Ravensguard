# Heists and jailbreaks: gaols, strongrooms and vaults as sites, guards on shifts, locks, keys, bribes, disguise, diversions, stealth and hearing in the dark

Plan refs: §3b (heist sites in every castle and town: castle dungeons and prisons, a lord's treasury, guild vaults, town undercrofts and drains; guards follow patrol schedules and sleep in shifts; locks, keys, bribes, disguise (wearing a lord's livery), diversions and stealth in the dark; break a captured companion out, or rob a vault; consequences through the crime and witness system: bounties, a manhunt), §3j (treasuries are physical: chests can be raided), §3.7 (ways out of captivity: rescued by companions). Phase 4 moves: heist sites (decided with the user), indoor fights that use walls (site fights used an open arena), the weather fields for stealth and hearing, the captors' camp not generated in the live area; Phase 5 moves: per-cell light at night, weapon condition set by fights and the broken brittle blade leaving the pack. **Done-when:** a jailbreak works.

## 1. Purpose
Turn the places where coin and prisoners are kept into crawlable sites built from blueprints, guarded by people who keep hours, sleep, carry keys and can be bribed, deceived or drawn away; and make getting in and out a matter of light, noise, walls and nerve. A jailbreak or a theft from a vault ends in the crime system: a bounty, a manhunt, a chronicle entry with causes.

## 2. Dependencies
| Needs | From |
|---|---|
| Underground levels, portals, the crawl (`SiteCrawl`), traps, the auto-map, `LightGrid` | Phase 1, 4 |
| `FightWorld` with sites (`FightWorld.Site`), watch `subdue` | Phase 4 |
| Guards, shifts, gaol, crime records, seen/unseen | `crime-and-justice.md` |
| Treasuries as purses, guild chests, relics | Phase 2, 5, `faith-and-church.md` |
| Officials' honesty (bribes) | `rule-and-income.md` 4.8 |
| Fire (a diversion) | `fire-and-burning.md` |
| Clothing layers (livery as the cover layer) | Phase 3 |
New: `Sim/Heists/` (`HeistSiteGen`, `GuardRoster`, `Noise`, `Locks`, `Disguise`, `Manhunt`), content `blueprints/underground/heist.jsonc` (about 12 templates), `heists/locks.jsonc`, livery garments. Extended: `FightWorld.Site` (walls and doors), `LightGrid` (per-cell light at night in the street and in sites), `SiteBook` (heist sites are permanent, not cleared).

## 3. Data
- **Heist sites** (generated on first visit like Phase 4 sites, from the place's building): the **gaol** (manor cellar or town gaol: 2-6 cells, a guardroom, the gaoler's key board), the **strongroom** (the fief treasury chest and the barn's tally; in a castle the keep's lowest level), the **guild vault** (the guild chest, the masters' secrets as manuscripts), the **undercroft** (a town's cellars joined by passages) and the **drain** (z -1 culverts under the main streets, entered by grates; a way into the gaol or the strongroom where the template has one). Each template: rooms, doors with lock quality, patrol waypoints, light sources (sconces lit 18-06), hiding spots (behind casks, under stairs).
- **`GuardRoster`** per site: guards (persons), shifts (day 06-18, night 18-06 with half the men), the patrol loop and its period (a full loop every 6-12 game minutes), sleepers (25% of the night shift after 02:00, in the guardroom), key holders (the gaoler holds cell keys; the treasurer the strongroom's).
- **Locks** (`heists/locks.jsonc`): quality Crude..Masterwork -> pick time (sim-s) 20 / 40 / 60 / 120 / 240 and difficulty (the Phase 4 lockpick rule's skill offset 0 / -10 / -20 / -35 / -50); bars and chains; a key opens instantly; a door can be broken (material HP, noise 25 m).
- **Noise sources** (metres heard in still air): walk 6, run 15, sneak 2, a door opened 8, a lock picked 4, a door broken 25, a fight 30, a shout 40, a thrown stone landing 10. **Hearing:** range x weather (Phase 4 row: light rain x0.85, heavy rain x0.7, storm x0.5) x 0.5 per wall or closed door between; a sleeper hears at x0.3.
- **Livery** (garments): a tabard or surcoat in a lord's colours (palette from the fief's banner), worn as the cover layer.

## 4. Rules

### 4.1 The captors' camp and gaols as places
Bandit captives are held in a real camp (a tent or a cave site with a stake line and two guards) in the live area (Phase 4 open item); fief gaols are heist sites; slaver pens are yards with a gate. All feed their prisoners (`crime-and-justice.md` 4.5).

### 4.2 Guards' behaviour
On shift a guard walks his loop or stands his post; every 5 s he **looks** (crime 4.1's seeing rule with indoor ranges: 12 m lit, 4 m dark) and **listens** (noise above). A sound heard -> **Investigate**: walk to the sound's cell, look around 20 s, return. Something seen out of place (an open cell, a body, a missing key, a guard down) -> **Alert**: shout (noise 40: every guard who hears it comes), lock the outer door, light more torches. An intruder seen and recognised as not belonging -> the hue and cry, pursuit, arrest by `subdue` (crime 4.4). Guards wake at a shout or a fight.

### 4.3 Getting in and through
- **Stealth in the dark:** the per-cell light grid (Phase 5 carry-over: night light per cell in streets and sites from sconces, windows, fires and carried torches) decides the seeing range; dousing a sconce (Q, 2 s, noise 2) darkens its cells; the hero's own torch (V) lights him.
- **Locks and keys:** pick with lockpicks (the Phase 4 rule, stream 0x455, time by the lock's quality); take a key from a sleeping or downed key holder (Q > Search: 6 s, noise 2; a sleeper wakes with P = 10% per search, stream **0x6B1**); keys are items and work until the lock is changed (a lord changes locks after a theft).
- **Bribes** (the one rule for every official): an offer of at least `3 x daily wage x (60 - honesty) / 20` mites (honesty 20: 6 x wage) is accepted with P = `(60 - honesty)` % (stream 0x613, key official and day); a guard or gaoler of honesty >= 60 never takes one; a refused bribe is a crime (bribery) and an alert.
- **Disguise:** wearing the lord's livery, a guard does not challenge the hero at more than 4 m unless he knows his face (familiarity >= 1) or the hero runs, carries a body, or is in a cell block; the cover layer is matched as in crime 4.1's descriptions (a witness's description names the livery: the lord's own guard is then sought).
- **Diversions:** a thrown object (noise at its landing), a fire set elsewhere (guards respond to a fire: `fire-and-burning.md`), a riot, a drunk companion picking a fight at the gate: guards investigate the nearest noise first.
- **Drains and climbing:** a grate takes 30 s with a prying tool (noise 8); ropes and ladders (Phase 4 items) reach windows and walls.

### 4.4 Indoor fights use walls
Fights inside a heist site or any building run on the site's real map: walls and closed doors block movement, missiles and sight; doors can be held (a guard in a doorway); the Phase 4 site arena's open area is replaced by `SiteMap` cells. The fight's settle path writes **weapon condition** back to the item rows (Phase 5 carry-over: wear and a broken brittle blade leave the pack at the barrier).

### 4.5 The jailbreak and the manhunt
A cell opened (key, pick, bribe, broken bars at noise 25) frees its captives (`CaptiveState.Rescued`, Phase 4 rescue rules: the rescued joins the party, takes control if the hero was the captive). Leaving the site unseen ends it quietly: the escape is found at the next guard round (the crime `jailbreak` with no witness: no bounty, a rumour). Seen: a bounty for each recognised person, and a **manhunt**: the fief's guards search a radius growing by 1 km an hour for 1 game day from the gaol (road checks: every guard on a road within the radius looks at passers-by by the crime 4.1 rule; a described person is matched by clothes), then give up; the bounty stays. Vault thefts work the same, with the stolen coin recorded as the victim's loss (the treasury's purse: real coin moved; the crime's value).

### 4.6 Tick placement
| Where | What |
|---|---|
| Live tick (20 Hz; guards' looking and listening at 5 Hz) | guards, noise, light, locks, fights |
| Barrier | site generation on first visit, keys and bribes, escapes, the jailbreak crime, the fight's settle (weapon condition) |
| H5 hour 0 (and hourly during a manhunt) | manhunt radius, road checks in the abstract |

## 5. Determinism
Streams: **0x6B0** patrol period jitter (key site and night), **0x6B1** sleepers waking at a search, **0x6B2** vault contents beyond the purse (goods and papers at generation, key site), **0x6B3** a disguised hero's challenge when the rule is borderline (key guard and hour); lock picking reuses 0x455, bribes 0x613. Noise and sight have no draws. `HeistHash` covers rosters, keys, lock changes and the manhunt.

## 6. UI (960 x 540)
The HUD's eye shows Seen / Unseen and an **ear** (noise you make now: Silent, Quiet, Loud) with the nearest listener's state (asleep, listening, investigating, alert); the auto-map marks heard guards; lock picking shows its time and chance; the cover layer slot names the livery's lord. Readable at 960 x 540.

## 7. Tests (golden)
| id | Case | Expected |
|---|---|---|
| HJ-1 | Generation | every manor and town in seed 1234 has a gaol site; every town a strongroom; guild towns a vault; the same template output on Windows and Linux (`HeistHash`) |
| HJ-2 | Shifts | night shift is half the men; 25% asleep after 02:00; a loop's period within 6-12 game minutes |
| HJ-3 | Hearing | a lock picked (4 m) through one wall (x0.5) is heard at <= 2 m; in heavy rain at <= 1.4 m; a sleeper at x0.3 |
| HJ-4 | Investigate then alert | a thrown stone lands 6 m from a guard: he walks there and returns; an open cell seen: a shout, every guard who hears comes |
| HJ-5 | Keys | a key taken from a sleeping gaoler opens the cell at once; after the theft the lord changes the locks |
| HJ-6 | Bribe | a gaoler of honesty 20 and wage 150: 900 mites accepted with P 40%; 600 refused with an alert; honesty 70 refuses any sum |
| HJ-7 | Disguise | in the lord's livery the hero passes a guard at 6 m; at 3 m he is challenged; a guard who knows him sees through it |
| HJ-8 | Indoor fight uses walls | a crossbow bolt does not pass a wall; a guard holding a doorway blocks two attackers; weapon wear written back at the barrier |
| HJ-9 | **Done-when: a jailbreak works** (OV-3) | a companion jailed for theft; at night the hero enters by the drain, picks the cell (Masterwork: 240 s), leaves with him unseen: `CaptiveState.Rescued`, the crime `jailbreak` found at the next round, no bounty; seen on the way out: bounties for both and a manhunt of 1 game day |
| HJ-10 | Vault | the strongroom's chest emptied: the treasury purse falls by the coin taken, the crime's value equals it, coin conserved |
| HJ-11 | Captors' camp | a bandit captive is held at a real camp site in the live area with two guards and fed from the stash |
| HJ-12 | Determinism, save | save mid-heist (a guard asleep, a key in the pack, a lock half picked) -> load -> save byte-identical; `HeistHash` equal |

## 8. Open questions (recommended defaults)
1. **Do lords change locks after a theft?** Yes, within 2 days (a locksmith's job). Default as written.
2. **Manhunt length:** 1 game day of active search, the bounty after that. Default as written.

## 9. Budgets
Guards' look and listen <= 0.05 ms per guard at 5 Hz (about 12 guards in a site: 0.6 ms a 5 Hz step, 0.15 ms a tick average); noise propagation <= 0.05 ms an event (walls counted on a cached cell path); a heist site generation <= 30 ms; per-cell light at night <= 1 ms a frame; save: rosters and keys <= 5 KB a site visited.
