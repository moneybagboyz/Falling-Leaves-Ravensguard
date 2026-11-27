# War screens and arms in play: the Army tab, the Armoury, the Fief screen, the helm, and the arms trade with real goods

Status: design, before any Phase 8 code, on top of f2530c3.
Plan refs: §3.9 (the Ledger; the Fief tab: zoning, taxes, levies; the Army tab: garrisons, retinue, wages), §3d (armouries, refit), §3j (supplies and money), §2.7 (UI as thin views).
Carried in (phase-7 review): the helm HUD, the Army tab, the Fief and Armoury screens (AE3), orders as supply contracts, physical goods in the arms trade, the steward's order, refit by commission, `Disband`/`Fund` leftovers (P7R-9).

## 1. Goals
1. The player sees and runs war through screens, not the dev console: the Army tab, the Armoury, the Fief screen and the helm.
2. Arms move as goods: orders are contracts, deliveries are carts and ships, and every kilogram is in the `MassLedger`.

## 2. What exists and what is new
| Today (f2530c3) | Phase 8 |
|---|---|
| `Game/UI/LedgerScreen.cs`, `IndustryTab.cs`; the `Armoury` key context (Phase 7 keys) with no screen | **new:** `UI/ArmyTab.cs`, `UI/ArmouryScreen.cs`, `UI/FiefScreen.cs` additions, `UI/HelmHud.cs` |
| `WarSupply/ArmsWorld.cs` (orders as rows; the arms trade by ledger only), `HostSupply.Arms.cs` | **extended:** orders as Phase 6 supply contracts; deliveries as cargo |
| `Soldiers/*` (templates, refit by hand) | **extended:** refit by commission |

## 3. Rules

### 3.1 The screens (WS-1)
- **Army tab** (Ledger): one row per warband, host, garrison and retinue: men by role, wages a season (mites), arrears, morale, supply days, location and task. Alerts: arrears over 10 days, supply under 5 days, desertion this week.
- **Armoury screen** (the Phase 7 `Armoury` context and keys): soldiers down the left; slots across; the template's gaps in red; F fill all, G gaps only, T edit the template, Enter to refit one man by hand.
- **Fief screen** additions: the levy by village (who owes, who was drawn), purveyance tallies due, scutage owed and paid, the mint and its master, depots and their stocks.
- **Helm HUD** (Mounted context at the helm): wind direction and speed, heading, speed over ground, sail set, sea state, water in the hull, crew orders (row, ship oars, ready grapples, board).
- All four are views over snapshots: no rule lives in the screen. 960x540 is the floor.

### 3.2 Arms orders as contracts (WS-2)
- A lord's seasonal arms order (Phase 7's `ArmsWorld.Order`, inside a tenth of the treasury) becomes a **supply contract** (Phase 6 contracts v2) offered to armourers and works in the realm, then abroad.
- **Delivery is real:** the maker's goods go by cart or ship to the depot. They are in transit in the `MassLedger` and can be robbed.
- **Pay** on delivery from the treasury purse; a late delivery pays 10% less a week late, down to 50%.
- **The arms trade** between realms at war moves goods the same way; the export ban stops the contract, not just the coin.

### 3.3 The steward's order and refit by commission (WS-2)
- **The steward's order:** a hired steward places the seasonal arms order within a budget the player sets (mites a season) and the gaps of the templates.
- **Refit by commission:** the player names a template and a budget. An armourer is hired by contract to buy the missing kit and fit it at the armoury. The labour is the armourer's hours; the parts are bought goods.

### 3.4 Leftovers to their source (P7R-9)
`Disband` and `Fund` return unspent coin to the purse it came from (the lord's, the realm's or a loan's), by a ledger transfer with `TransferReason.Subsidy`, never to the realm treasury by default.

### 3.5 Integer math notes
- Late pay: `pay = price x max(500, 1000 - 100 x weeks_late) / 1000`, integer mites.
- Screens format through `MoneyText`; no sums are made in the UI.

## 4. Saves
Additive keys: `warsupply` (contract ids for orders, the steward's budget), `contracts` (the `arms_supply` terms). No new section.

## 5. Golden tests
| Id | Name | Asserts |
|---|---|---|
| WS-1 | Army tab | rows match the army book for a world with 2 warbands, a host and a garrison; every total equals the book's |
| WS-2 | Armoury | F fills from stock best first; G fills only gaps; the screen moves no item itself (the desk does) |
| WS-3 | Fief screen | the village levy, tallies, scutage and depots shown equal the books |
| WS-4 | Helm HUD | wind, heading and speed equal the ship frame's at 960x540 |
| WS-5 | Views only | the screens hold no state across a save (a load restores the same view) |
| WS-6 | Order as contract | a season's order is offered, taken, made and delivered; coin and mass exact |
| WS-7 | Robbed in transit | a cart of 200 kg of kit robbed on the road: the kit is the robbers'; the order is short |
| WS-8 | Late pay | 3 weeks late pays 70%; 8 weeks pays 50% |
| WS-9 | Steward and commission | the steward's order stays within budget; a commission fills a template's gaps from bought goods |
| WS-10 | Leftovers | `Disband` returns unspent coin to its source purse; coin exact |

## 6. Parts and gates
| Part | Scope | Gate | Months |
|---|---|---|---|
| P8-WS-1 the war screens | Army tab, Armoury, Fief, helm | WS-1 to WS-5 | 0.9 |
| P8-WS-2 arms in play | contracts, real deliveries, the steward, commission, leftovers | WS-6 to WS-10 | 0.9 |

WL-9 and EA_F11 are measured after WS-2 in `08-balance-pass.md`.
