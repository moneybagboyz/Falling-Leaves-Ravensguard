# A Realistic Medieval Economy — Design Proposals

_Written from the perspective of an economist advising on simulation design. This document is grounded in the actual economic history of medieval Europe (roughly 900–1500 CE) and translates that history into concrete simulation proposals for Ravensguard. Every suggestion is tied to a real historical mechanism and graded by implementation complexity._

---

## Preface: Why Most Game Economies Fail at Being Medieval

Most game economies that call themselves "medieval" are actually early-modern or even industrialist in disguise — they have reliable markets, stable prices, predictable income, and no transaction costs. The real medieval economy was defined by four structural facts that games almost never model:

1. **Land was the only real capital.** There were no liquid capital markets. Investment meant acquiring land or physical goods. Return on investment was rent, harvest, and toll — not interest on a deposit.
2. **Information was catastrophically bad.** Merchants did not know prices in distant cities. Farmers did not know whether the harvest two provinces over had failed. Decision-making was based on rumour, tradition, and hard-won personal relationships.
3. **Transport costs were crushing.** Moving goods by road cost roughly 10× more per tonne-kilometre than moving them by river, and 30× more than sea transport. This meant most settlements were near-autarkic. Region-level integration existed for high-value goods only, not bulk staples.
4. **Institutions were weak and local.** Property rights varied by charter, lord, and custom. Contracts were enforced through reputation and threat, not courts. Guilds, church, and lord all extracted rents simultaneously from the same producer.

If your simulation doesn't hurt to live in economically, it probably isn't medieval enough.

---

## 1. The Manorial System: Land as the Root of Everything

### 1.1 The Historical Reality

The manor was not just a settlement — it was an economic contract. A lord held land in exchange for military service upward. Peasants held strips of land in exchange for labour service, rent in kind, and later money rent. The price of this land tenure was not set by supply and demand in any market sense. It was set by custom, by inertia, and by the threat of eviction or violence.

This creates a radically different economic baseline than a free market. Peasants were not free to sell their labour to the highest bidder. Land could not simply be bought and sold. The allocation of productive resources was determined more by social hierarchy and legal tenure than by price signals.

### 1.2 Implementation Proposals

**Acreage tenure categories** — Instead of treating all worked acres as equivalent, distinguish:

| Tenure Type | Control | Rent Obligation | Mobility |
|-------------|---------|----------------|---------|
| Demesne | Lord-controlled | None (produces for lord) | N/A |
| Villein strip | Peasant-worked | Labour days + rent in kind | Near zero |
| Free tenure | Free peasant | Money rent | Limited |
| Waste/common | Communal | No rent, but use-right fees | Open access |

Demesne land produces directly into the lord's (faction's) inventory. Villein land produces into peasant inventory but a fraction is transferred to the lord as rent. Free tenure peasants pay coin rent instead, giving them more market flexibility. Waste land is extracted from but not "owned" — it degrades over time if over-exploited (see §8 on common pool resources).

**Rent extraction as a parallel drain** — Currently the settlement pulse generates coin income per class. Extend this: a **rent ledger** should track how much coin and goods flow upward from peasants → lords → faction treasuries each pulse. This is not optional income — it is a feudal obligation. Failure to pay drives legal pressure, not just unrest.

**Labour services (corvée)** — A fraction of peasant labour is owed to the lord before any free production can occur. Effectively this is a `labour_tax_fraction` that reduces the peasant's effective `labor_days_per_head_per_tick` available for their own fields. Historically this ranged from 2–3 days per week on some manors to a few days per year on others, depending on period and region.

---

## 2. Seasonal Economics: The Medieval Calendar Was the Economic Calendar

### 2.1 The Historical Reality

Nothing in a medieval agrarian economy was constant across the year. Prices, labour availability, activity types, and survival risk were all violently seasonal:

- **Spring planting (March–May):** Grain stocks from the previous harvest are nearly exhausted. Prices spike. Peasants often had to borrow grain from the lord or a moneylender to survive until harvest. This is the "spring hunger" — historically the most dangerous season for the poor.
- **Summer (June–August):** Harvest approaches. Labour demand for the lord's demesne (corvée) peaks. Markets slow because everyone is working.
- **Harvest (August–October):** Grain floods the market. Prices crash immediately after harvest — by as much as 50–70% in a good year. This is the only moment when peasants could sell surplus, but peasants also had to pay rents at harvest, so they were forced sellers at the worst moment.
- **Winter (November–February):** No outdoor farming. Craft production peaks. Livestock slaughtered at Martinmas (November) because fodder runs short. Preserved meat was a winter staple but required salt — a highly strategic good.

### 2.2 Implementation Proposals

**A four-phase calendar** keyed to the strategic tick:

```
Spring (ticks 0–24):    yield_multiplier = 0.6,  price_pressure = +30%
Summer (ticks 25–49):   yield_multiplier = 0.8,  price_pressure = +10%
Harvest (ticks 50–74):  yield_multiplier = 1.6,  price_pressure = −40%
Winter (ticks 75–99):   yield_multiplier = 0.0,  price_pressure = +50%
```

These multipliers apply to `base_yield_per_acre_per_tick` in `ProductionLedger`. Winter agriculture is `0.0` — fields do not produce. Winter craft and extraction output can be boosted instead (smiths work indoors; loggers work when sap is low and wood is drier).

**Forced selling at harvest** — The system should trigger a compulsory rent payment event at the end of harvest season. Peasants sell grain to pay rent, regardless of the current price. This is historically why grain prices crashed at harvest even in years of moderate yield — it was not voluntary market behaviour.

**Spring hunger mechanics** — If grain inventory falls below a threshold by the end of winter (approximately 7 days' supply), a **subsistence crisis** flag is raised. This triggers:
- Accelerated unrest accumulation
- Population death (small but real — 1–3% of the most vulnerable class)
- Emergency borrowing demand (increases coin deficit)

This pressure forces players and the simulation to plan ahead — hoarding grain into winter is not greed, it is survival planning. Without this, the economy feels flat and safe.

---

## 3. Transport Costs: Why Most Goods Never Left Home

### 3.1 The Historical Reality

Gregory Clark and others have estimated medieval road transport costs at roughly 0.5–1 penny per tonne-mile. A bushel of wheat weighs about 27 kg. Moving it 100 miles by road therefore cost approximately as much as the grain was worth at source prices. The rule of thumb historians use: **grain could not be profitably moved by road more than about 100 km from its origin**.

River transport was 5–10x cheaper. Sea transport was cheaper still. This is why medieval trade routes followed water so obsessively — the Po Valley, the Rhine, the Thames, the Seine. Cities that lacked river access remained small. Cities at river mouths became dominant.

The practical consequence: **most bulk goods (grain, timber, hay, peat) were locally produced and locally consumed**. Only high-value, low-weight goods (spices, fine cloth, dyes, silver, glass) generated long-distance trade.

### 3.2 Implementation Proposals

**Freight cost as a route property** — Each route edge in `WorldState.routes` should carry a `transport_cost_multiplier` based on terrain and road quality:

| Route Type | Cost Multiplier | Example |
|------------|----------------|---------|
| Sea route | 0.05 | Coastal ports |
| River route | 0.15 | Along navigable rivers |
| King's road (maintained) | 0.40 | Major arterial routes |
| Forest track | 1.20 | Unimproved wilderness paths |
| Mountain pass | 2.50 | High-altitude crossings |

**Freight cost deducted from trade profit** — When a trade party arrives at its destination, the effective sale price of the cargo should be:

$$P_{effective} = P_{destination} - (P_{destination} \times C_{route} \times D_{tiles})$$

Where $C_{route}$ is the route's cost multiplier and $D_{tiles}$ is route length. If the freight cost exceeds the price differential, the trade party delivers anyway (the spawner shouldn't have sent it), but the sending settlement receives a penalty — a lesson for the next dispatch decision.

**Value density as a dispatch filter** — The `TradePartySpawner` should refuse to dispatch bulk low-value goods (timber, wheat) over routes longer than a threshold, while high-value goods (iron tools, cloth, coin) can travel farther. This naturally produces the historically observed pattern: regional grain markets, continental luxury goods markets.

**Road investment as a settlement upgrade** — Lords historically built and maintained roads because better roads increased their tax base. A settlement or faction should be able to invest coin in improving a route, permanently reducing the freight cost multiplier on that edge.

---

## 4. Price Formation: Inelastic Demand and Market Thinness

### 4.1 The Historical Reality

Modern economists are trained to think of price elasticity of demand — as prices rise, consumers buy less. For staple food in the medieval period, this elasticity was nearly zero. You cannot eat less than your subsistence minimum and survive. A peasant whose grain supply halved did not buy half as much grain — they bought the same amount, went into debt, sold possessions, or died. This **inelastic demand for staples** means small supply shocks produce violent price movements for food, historically 3–5× in bad harvest years.

Markets were also **thin** by modern standards. A typical village market served a radius of roughly one day's walk (~25 km). Prices in villages 50 km apart could differ by 50–100% for the same good because information didn't travel and transport was too expensive to arbitrage the gap. Market integration (price correlation between distant markets) only emerged slowly through regular fairs and eventually through merchant correspondence networks.

### 4.2 Implementation Proposals

**Price elasticity parameter per good** — Add an `elasticity` field to each good's JSON definition:

```json
{
  "id": "wheat_bushel",
  "elasticity": 0.15
}
```

A low elasticity (0.15) means price rises steeply even with modest supply deficits. Luxury goods (cloth_bolt: 0.7) respond more proportionally. The `PriceLedger` formula would incorporate this:

$$P_{target} = V_{base} \times \left(\frac{D}{S}\right)^{1/\varepsilon}$$

With $\varepsilon = 0.15$, a 20% supply shortage causes a price spike to approximately 3.6× base value — which is historically plausible for a moderate grain failure.

**Market integration radius** — Settlements should only be price-aware of settlements they are connected to by a route. Currently this is already architecturally the case (the spawner only looks at direct connections), but the price mechanism should go further: isolated settlements should have **no price discovery** from distant markets. Their prices are driven purely by local supply and demand, not by what is happening across the region.

**Fairs as periodic market integration events** — Historically, annual fairs (Champagne Fairs, St. Bartholomew's Fair) were the mechanism by which distant prices briefly equilibrated. A settlement with a Market building could host a Fair event once per year that:
- Temporarily extends its price discovery radius by 3–5 route hops
- Attracts trade parties from farther settlements for a limited window
- Generates coin income for the host settlement (toll revenue)

---

## 5. Money, Credit, and the Limits of Monetization

### 5.1 The Historical Reality

The medieval economy was only partially monetized, and that monetization was uneven across time and space. Key facts:

- **Coin was often scarce.** The money supply contracted dramatically between the fall of Rome and the commercial revolution of the 12th century. Many transactions were conducted in kind (rent paid in wheat, labour paid in food, debts settled in livestock).
- **Credit was ubiquitous but informal.** Peasants ran perpetual credit accounts with their lord, with their neighbor, with the miller. These debts were tracked in ledgers, settled seasonally, and could be inherited. The absence of formal banking did not mean the absence of credit.
- **Usury was prohibited** by the Church but practiced everywhere. Italian merchant banks (Lombards) invented instruments — bills of exchange, partnerships, deposits — explicitly to disguise loans as something the Church could tolerate.
- **Coin debasement** was how medieval kings financed wars. By reducing the silver content of coins, a king could effectively tax anyone holding coin. This eroded trust in currency and periodically triggered inflation.

### 5.2 Implementation Proposals

**A debt ledger alongside the inventory** — Each settlement (and eventually each person) should carry a debt dictionary:

```
debts: { creditor_id → { principal: float, interest_rate: float, due_tick: int } }
claims: { debtor_id → { principal: float, interest_rate: float, due_tick: int } }
```

When a settlement cannot pay rent or purchase goods, it does not simply refuse — it takes on debt. Debt above a threshold triggers legal pressure (the lord seizes assets), social disfavour (merchants refuse credit), and eventually either debt bondage or flight.

**In-kind transactions for low-monetization regions** — Hamlets and early-tier villages should have a `monetization_rate` (0.0–1.0) that determines what fraction of transactions occur in coin versus in-kind barter. A low-monetization settlement cannot use coin-priced goods effectively — it must trade wheat for tools directly, which is less efficient than using coin as an intermediary. This creates a strong incentive for rulers to develop money economies in their territories.

**Credit as a seasonal survival mechanism** — In spring, if a settlement's grain inventory drops below the survival threshold and it cannot purchase grain on the market, it should seek credit from the nearest connected settlement with surplus. The interest represents the risk premium on an unsecured food loan. Historically this was typically 20–40% annual interest for grain loans — predatory but available.

**Coin debasement** — A faction with a treasury below a threshold can choose to debase its coinage. This inflates prices in all its territory (because coin is worth less), temporarily fills the treasury, but reduces monetization trust for several years afterward. Merchants demand a premium to transact in debased coin.

---

## 6. Land Markets and Factor Mobility

### 6.1 The Historical Reality

The Black Death of 1347–1353 killed 30–60% of Europe's population. The economic consequence was a dramatic shift in factor prices: land became abundant and labour became scarce. Wages rose sharply. Land rents fell. Lords who had gotten rich on labour-intensive demesne farming suddenly couldn't find workers. This forced a historic transition from labour rent to money rent — the villein system broke down within a generation in many regions.

This tells us something important: **factor markets respond to population shocks in ways that fundamentally restructure the economy**. A game that models only goods markets is missing half the picture.

### 6.2 Implementation Proposals

**A labour market, not just labour as a production input** — Labour should have a price (wage rate) that rises when workers are scarce and falls when there is surplus population. The settlement's effective labour supply:

$$L_{effective} = \sum_{c \in classes} N_c \times l_c \times (1 - t_{labour})$$

Where $l_c$ is `labor_days_per_head_per_tick` and $t_{labour}$ is the corvée tax fraction. When demand for labour from buildings exceeds $L_{effective}$, wages rise. This rising wage is felt as a cost by buildings (upkeep in coin increases) but as income by workers (their `coin_income` rises above baseline).

**Serfdom vs. free labour as a legal institution** — Settlements should have a `labour_regime` property:

| Regime | Labour Mobility | Wage Level | Lord's Labour Claim |
|--------|----------------|-----------|---------------------|
| `serfdom` | Near zero | Subsistence | High (corvée) |
| `villeinage` | Low | Below market | Medium |
| `free_tenure` | Regional | Market rate | Low |
| `wage_labour` | Full | Market rate | None |

After a major population die-off (plague, famine, war), settlements under serfdom experience labour shortages that eventually force legal liberalization — a historical accuracy mechanism.

**Population flight** — When `unrest > 0.6` and there is a connected settlement with significantly lower unrest and higher prosperity, a fraction of peasants should migrate there each pulse. This is historically the primary check on lord overextraction — serfs could vote with their feet despite legal constraints. Lords who overtaxed simply watched their villages empty.

---

## 7. Guilds and the Control of Markets

### 7.1 The Historical Reality

Medieval urban economies were controlled by guilds. Guilds set prices (often above market clearing), controlled entry into trades (long apprenticeships, expensive mastership fees), maintained quality standards, and collusively divided markets. They were simultaneously a consumer protection institution, a cartel, and a form of social insurance for their members.

The economic effect of guilds was fundamentally **anti-competitive**: they raised prices above what a free market would produce, restricted supply, and prevented innovation (any technique not approved by the guild could result in prosecution). They also provided genuine services: quality assurance, training infrastructure, and care for sick or widowed members.

### 7.2 Implementation Proposals

**Guild licences as a settlement property** — Tier-2+ settlements have a `guild_licences` dictionary mapping craft type to guild presence:

```
guild_licences: { "blacksmith": true, "cloth_weaver": true, "miller": false }
```

Where a guild exists:
- Production of that good is limited to guild-member buildings
- Price cannot fall below guild minimum (a floor above `PRICE_FLOOR`)
- Building construction in that craft requires a coin payment to the guild
- Guild coffers can fund social insurance (reducing unrest spikes from individual shortfalls)

Where no guild exists, production is free but quality is lower (expressed as a `quality_modifier` on finished goods), and prices are more volatile.

**Guild conflict with lords** — Guilds historically fought for and against royal/noble control. A powerful guild can refuse to supply the lord's army (an economic embargo). A lord can dissolve a guild and seize its assets (a short-term revenue windfall that destroys long-term craft production). This creates a genuine political-economic tension.

---

## 8. Commons, Enclosure, and the Tragedy of Common Resources

### 8.1 The Historical Reality

The common fields — the pasture, the forest, the fishpond — were not owned by any individual. They were used collectively under customary rules that were enforced by the village community, not by law. These rules generally kept usage below the resource's regeneration rate. The commons worked reasonably well for centuries because the community that used them also policed them.

The problem arose when external agents (lords with legal power, wealthy peasants with enough capital to bribe officials) began enclosing commons — converting communal land to private ownership. This produced short-term efficiency gains for the encloser and long-term catastrophe for the dispossessed, who had no fallback.

**The tragedy of the commons** (Hardin's famous formulation) is an overstatement for the medieval period — Elinor Ostrom showed that common-pool resources are often managed sustainably by the communities that depend on them. But under pressure (population growth, external market demand for wool, political disruption), the management rules can break down.

### 8.2 Implementation Proposals

**Woodlot and pasture as depletable resources** — Currently woodlot acres are a fixed production input. They should be a **resource pool** that depletes with use and regenerates when rested:

```
cell.resource_pools: {
    "wood": { current: 850.0, max_capacity: 1000.0, regeneration_per_tick: 2.5 },
    "ore":  { current: 320.0, max_capacity: 500.0,  regeneration_per_tick: 0.1 }
}
```

Extraction that exceeds regeneration draws down the pool. When the pool reaches zero, yield drops to near zero even if labour is available. This creates a real temporal scarcity — early, easy extraction eventually forces conservation or new resource discovery. Iron ore regenerates almost nothing (geological timescale) while wood regenerates at moderate speed.

**Enclosure as a player/faction action** — A lord can enclose common land, converting it from `waste` to `demesne` or `free_tenure`. This:
- Increases lord's direct income from that land
- Eliminates peasant access to subsistence resources (firewood, grazing, gleaning)
- Drives a unrest spike in the settlement
- Potentially triggers population flight

This is historically accurate and creates a genuine moral-economic dilemma: enclosure enriches elites and drives agricultural efficiency but immiserates the rural poor and destabilizes the settlement.

---

## 9. Market Institutions and Transaction Costs

### 9.1 The Historical Reality

Markets did not spontaneously exist. They were created by legal decree, typically by a lord who charged a toll on every transaction. A weekly market was a franchise — a lord paid the king for the right to hold it, then charged merchants a percentage of sales (typically 1–5%). This toll was the lord's return on investment, and the market's continued existence depended on the lord's willingness to protect traders from bandits and his own soldiers.

Transaction costs — the costs of finding a trading partner, agreeing terms, and enforcing the contract — were enormous by modern standards. The merchant networks that eventually solved this problem (Italian merchant banks, the Hanseatic League) did so through personal reputation networks and violent self-enforcement, not through legal institutions.

### 9.2 Implementation Proposals

**Market toll as a revenue stream** — Every trade party that passes through a settlement with a Market building pays a `market_toll_rate` (e.g., 3%) of their cargo's value to that settlement's coin inventory. This makes Market buildings a genuine revenue asset worth investing in, not just an unlock.

**Toll roads and bridges** — On specific route edges (particularly river crossings and mountain passes), settlements or factions can place a toll gate. All passing trade parties pay the toll. This creates:
- A legitimate source of income for controlling parties
- An incentive for merchants to route around toll collectors
- A military-strategic value to river crossings (blockading trade = economic warfare)

**Reputation as a transaction cost reducer** — Merchants and settlements that have traded successfully multiple times should accumulate mutual trust. The first transaction at an unfamiliar destination pays full transaction costs (higher price spread, no credit). Repeated trading reduces these costs. This models the historical importance of **merchant correspondent networks** — the reason Italian merchant families dominated medieval trade was that they had invested decades in building trust relationships that let them transact at lower cost than everyone else.

---

## 10. The Malthusian Dynamic: Population as Both Resource and Burden

### 10.1 The Historical Reality

Thomas Malthus (writing in 1798, but describing mechanisms that had operated for millennia) argued that population grows until it hits the capacity of the food supply, at which point rising prices and mortality checks it back down. This "Malthusian trap" describes medieval Europe extremely well for most of the period 900–1348.

During the population expansion of 1100–1300, land was brought under cultivation that previous generations had left as waste. Crop yields fell as marginal land was pushed into production. Real wages fell as labour became abundant. The agrarian system was running at full capacity — and then the Black Death killed between a third and a half of the population in four years.

The interesting observation is that the survivors of the Black Death were materially better off within a generation. Land was now abundant. Wages were high. The Malthusian pressure had been released.

### 10.2 Implementation Proposals

**The carrying capacity of a cell** — Each terrain cell should have an implicit **maximum sustainable population** derived from its `fertility`, `worked_acres`, and available `resource_pool`s. When a settlement's population approaches this ceiling, food prices rise (supply approaches maximum), wages fall (labour surplus grows), and unrest builds. This is the Malthusian pressure mechanism.

**Births and deaths as a quarterly demographic pulse** — Running at a slower strategic cadence:

$$\Delta P = P \times \left( b_{base} \times \pi - d_{base} \times \mu - d_{shortfall} \times S_{food} \right)$$

Where:
- $b_{base}$ — base birth rate (~0.03 per year, historical medieval)
- $d_{base}$ — base death rate (~0.025 per year in normal years)
- $\pi$ — prosperity (higher prosperity → lower mortality)
- $\mu$ — unrest modifier on death rate
- $d_{shortfall}$ — additional death rate from food shortfall (can be very large during famine)

Population growth drives production, which attracts more people, which drives land under marginal cultivation, which eventually diminishes yield, and the cycle closes. This creates endogenous economic cycles without scripting them.

**Epidemic events** - A periodic epidemic event (not just the Black Death — diseases were endemic and regular) should fire based on population density and sanitation proxies:

$$p_{epidemic} = k \times \frac{P_{total}}{A_{settlement}} \times (1 - \text{sanitation\_index})$$

An epidemic kills a fraction of all classes (weighted toward the youngest and poorest), releases accumulated economic pressure, and resets settlement dynamics.

---

## 11. Taxation and Fiscal Systems

### 11.1 The Historical Reality

Medieval taxation was not a clean annual income tax. It was a palimpsest of overlapping obligations:

- **Land rent** — owed by peasants to their immediate lord, in labour, kind, or coin depending on tenure type and period.
- **Tithe** — 10% of agricultural output owed to the Church. Genuinely collected in most places most of the time, in grain.
- **Tallage / taille** — An arbitrary levy a lord could demand at will from unfree tenants. This was the most resented.
- **Market and fair tolls** — Charged on all transactions.
- **Banalités** — Fees for using the lord's mill, his oven, his winepress. Mandatory use. High fees.
- **Royal taxation** — Rare in normal years but enormous during wars. Required by parliaments/estates. Often triggered crises.

What is notable is how many different entities were extracting from the same production. A peasant farming one acre might owe labour to the lord, grain to the Church, a milling fee to another lord, and an occasional royal levy. The **total extraction rate** across all obligations could easily exceed 50% of gross production in bad times.

### 11.2 Implementation Proposals

**A multi-tier fiscal ledger** — Track all extraction layers separately:

```
fiscal_obligations:
    land_rent:    { creditor: faction_id, rate_in_kind: 0.15, rate_in_coin: 0.0 }
    tithe:        { creditor: "church",   rate_in_kind: 0.10, rate_in_coin: 0.0 }
    market_toll:  { creditor: faction_id, rate_in_coin: 0.03, trigger: "trade" }
    banalite:     { creditor: faction_id, type: "mill",       cost_per_use: 0.5 }
    tallage:      { creditor: faction_id, frequency: "event", amount: "variable" }
```

This ledger is the primary sink on settlement production — it is where surplus goes before it reaches the market. A settlement under heavy extraction will never build reserves or invest in growth. A settlement under light or well-structured extraction can accumulate and invest.

**The Church as an economic actor** — The tithe gave the Church a 10% stake in all agricultural production. The Church used this income to fund hospitals, schools, roads, and (occasionally) actual religious functions. In game terms, the Church faction should collect tithe automatically from all settlements in its religious territory and spend it on institutions that reduce mortality and unrest — making it a net social good that is also politically powerful because it cannot easily be defunded.

---

## 12. The Putting-Out System and Proto-Industry

### 12.1 The Historical Reality

Before factories, manufactured goods were made through the **putting-out system** (Verlagssystem): a merchant-capitalist would supply raw materials (wool, flax, iron) to rural households and collect the finished goods (cloth, linen, tools) for a pre-agreed price. The household owned no capital except their labour. The merchant owned the raw materials and controlled the market.

This was not small-scale. By 1300, the Flemish cloth industry employed perhaps 100,000 people in this system. English wool was the raw material for most of it. The wool trade financed the English Crown's wars.

### 12.2 Implementation Proposals

**Merchant capital as a production enabler** — A Merchant-class population in a settlement with capital (high coin inventory) can finance putting-out production. This works as an additional production pathway beyond building-based recipes:

```
if settlement has Merchant population with coin_inventory > threshold:
    select a recipe that has raw_material inputs
    advance raw materials to connected rural settlements
    schedule a "collection" event N ticks later
    receive finished goods + pay households a wage
```

This creates a **merchant profit** (the gap between raw material cost + wages and the finished good's market price), which feeds back into the merchant class's coin income. It also creates rural employment that supplements subsistence farming — historically rural proto-industry was crucial for peasant survival because it provided winter income.

---

## 13. External Shocks: War, Plague, and Famine

### 13.1 The Historical Reality

The medieval economy was not a system in stable equilibrium periodically perturbed by external shocks — it was a fragile system where external shocks were the normal state and stability was the exception. Major recurring shocks:

- **Harvest failure** (every 3–5 years on average in pre-modern Europe) — due to drought, excessive rain, frost, or pest. A 25% harvest shortfall caused significant price rises. A 50% shortfall caused famine.
- **War and military extraction** — armies consumed everything. The 14th-century Hundred Years' War routinely destroyed 30–50% of the agricultural infrastructure of the regions it passed through.
- **Epidemic disease** — not just the Black Death. Dysentery, typhus, smallpox, and influenza killed regularly. Each epidemic could kill 5–15% of a regional population.
- **Credit crises** — multiple Italian banking houses collapsed in the 14th century (the Bardi and Peruzzi bankruptcies in the 1340s were triggered by Edward III defaulting on massive war loans). These cascaded as settlement-level credit networks seized up.

### 13.2 Implementation Proposals

**A shock event system tied to the strategic world pulse:**

| Event | Trigger | Economic Effect | Duration |
|-------|---------|----------------|---------|
| Harvest failure | Random (weighted by weather, terrain) | Yield × 0.4–0.6 for one season | 1 season |
| Military forage | Army passage through cell | Inventory −30%, worked_acres −20%, unrest +40% | 2–3 seasons recovery |
| Epidemic | High population density + low sanitation | Population −5% to −30%, labour scarcity, wages rise | 1–2 years |
| Credit seizure | Major trading partner defaults | Credit lines recalled, trade volume −50% for connected settlements | 3–6 months |
| Road destruction | War or neglect | Route freight multiplier ×3 until repaired | Until investment made |

These events should not be purely random — they should have preconditions that make them more likely. A settlement that has been running near food depletion for 3 pulses is a plague candidate. A settlement growing too fast on marginal land is a famine candidate. **Structured vulnerability** is more interesting than random disaster.

---

## 14. Summary: Priority Implementation Order

The proposals above range from immediately practical to long-term architectural. Here is an honest economist's priority ordering:

### Must-Have for Economic Credibility

1. **Seasonal yield multipliers** (§2) — without this, the economy has no rhythm and no danger period. The spring hunger is the emotional core of medieval economic life.
2. **Freight cost model on routes** (§3) — without this, trade has no geography. A wheat trade from 500 km away is as easy as 5 km.
3. **Depletable resource pools** (§8) — without this, production is perpetual motion. Wood and ore should eventually run out.
4. **Population dynamics — births, deaths, plague** (§10) — without this, the Malthusian pressure is absent and settlements grow forever without consequence.

### High Value, Moderate Complexity

5. **Debt and credit** (§5) — the spring survival loan is the most historically authentic economic relationship in the medieval world
6. **Multi-tier fiscal extraction** (§11) — reveals the actual structure of feudal economic power
7. **Labour scarcity and wages** (§6) — makes population shocks (plague, famine, war) economically consequential rather than just population number changes
8. **Enclosure as a player action** (§8) — creates genuine moral-economic decisions

### Rich Additions for Depth

9. **Price elasticity parameters** (§4) — makes staple food feel appropriately dangerous
10. **Guilds** (§7) — creates urban political economy; interesting player interaction surface
11. **Putting-out system** (§12) — connects merchant capital to rural production in a historically accurate way
12. **Mandatory fair events** (§4) — periodic integration moments that create trade opportunities
13. **Debasement** (§5) — faction-level fiscal desperation mechanism

### Long-Term Architecture

14. **Church as economic actor** (§11) — requires a religious faction layer
15. **Reputation-based transaction costs** (§9) — requires per-person or per-faction relationship tracking at scale
16. **Banalités and mill monopolies** (§11) — detailed enough to be interesting at tactical scale once buildings are deeply simulated

---

## 15. The One Principle That Governs All of These

Every proposal in this document derives from the same underlying economic truth about the medieval world:

> **Scarcity was structural and pervasive, information was local and unreliable, and institutions were extractive and weak.**

A game economy that makes resources abundant, prices stable, information perfect, and institutions supportive is not medieval. It is a fantasy version of modernity with crossbows.

The goal is an economy that creates constant, exhausting pressure on its participants — where survival requires skill and foresight, where prosperity is earned against the grain of the system rather than with its help, and where the player's economic decisions have genuine consequences that compound over time.

That is what medieval economics actually felt like from the inside. It is also what makes for the most interesting simulation.
