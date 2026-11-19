# Phase 4: Dwarf Fortress calibration notes (resolver R1)

The Dwarf Fortress wiki can't be fetched from the build environment (the page fetcher can't resolve the host). These notes record what a web search of the wiki returned, so the calibration task (resolver-v2 R1) has a fixed reference. Where the wiki is unclear or disagrees with itself, the decision is written down. Re-check against the live pages when they can be read.

## What the wiki says (from search results, Nov 2026)
- **Melee momentum** (DF2014:Weapon): `M = Skill × Size × Str × Vel / (10^6 × (1 + i_Size / (w_density × w_size)))`.
  - *Skill*, *Size* (creature body size) and *Str* (strength) are the attacker's.
  - *Vel* is the attack's velocity multiplier.
  - *i_Size* is the creature's size term against the weapon's mass (density × size).
  - Heavy weapons gain little once their mass is large relative to the wielder, which matches plan §2.6 ("diminishing returns: lighter weapons swing faster").
- **Edged layer test** (Material science): an edged attack cuts a layer when momentum clears a threshold built from the ratios of the layer's and the weapon's shear values (rSY, rSF). Plan §2.6 has the full form: `(rSY + (A + 1) × rSF) × (10 + 2 × Qa) / (S × Qw)`.
- **Failure:** if neither the edged nor the blunt threshold is met, the attack becomes blunt for good, and momentum is multiplied by `SHEAR_STRAIN_AT_YIELD / 50000` (edged) or `IMPACT_STRAIN_AT_YIELD / 50000` (blunt).
- **Shear fracture** is "the most important indicator of edged strength" (Weapon page).
- **Conflict:** the Material science page says shear yield "doesn't actually matter"; the Weapon and Combat pages use it for cutting. **Decision:** keep rSY in the formula (plan §2.6), with weight tunable in content, and let the calibration harness show whether it matters.
- **Edged metal ranking** (Combat page), worst to best: silver < copper < bronze < iron < steel < adamantine. **Golden test:** with equal skill, shape and quality, the cut rate against the same armour follows this order.
- **Sharpness:** blade materials are about 10× sharper than standard metals (Metal page).

## Calibration targets (resolver-v2 §8; keep these)
- Duels last 10–20 s.
- 40–60% of attacks are defended.
- A spear beats a sword of equal skill at least 55% of the time.
- With treatment, bleed-out is at most 35%.
- Severing is rare; armour matters: mail turns cuts to blunt blows, rigid plate soaks 98–99% of a stopped blow.

## What to re-check when the wiki can be read
- The exact *i_Size* term and the velocity multipliers per attack.
- The quality multipliers (Qw, Qa) and how sharpness enters the edged formula.
- The worked-example momentum table (battle axe by material) for the golden tests RS-1 onwards.
