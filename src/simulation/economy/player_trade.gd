## PlayerTrade — handles buy/sell transactions between player and market.
##
## Static API called from settlement_view dialog actions.
## All changes are applied to WorldState directly (no EventQueue needed
## for immediate transactions).
class_name PlayerTrade
extends RefCounted

## Trading skill level above which margins improve (0.5% per level).
const SKILL_MARGIN_PER_LEVEL: float = 0.005

## Maximum price discount a high-skill trader can achieve when buying.
const MAX_BUY_DISCOUNT:  float = 0.30
## Maximum price markup a high-skill trader can achieve when selling.
const MAX_SELL_PREMIUM:  float = 0.30

## Minimum quantity a player can buy in a single transaction.
const MIN_BUY_QTY: float = 0.5
## Minimum quantity a player can sell in a single transaction.
const MIN_SELL_QTY: float = 0.5


## Buy `quantity` units of `good_id` from settlement `ss`.
## Returns "" on success or an error string if the transaction fails.
static func buy(
		player:   PersonState,
		ss:       SettlementState,
		good_id:  String,
		quantity: float) -> String:

	if quantity < MIN_BUY_QTY:
		return "Quantity too small."

	var available: float = float(ss.market_inventory.get(good_id, 0.0))
	if available < quantity:
		return "Market does not have enough %s (has %.1f)." % [good_id, available]

	var base_price: float = float(ss.prices.get(good_id,
		ContentRegistry.get_content("good", good_id).get("base_value", 1.0)))
	var effective_price: float = base_price * _buy_multiplier(player)
	var total_cost: float = effective_price * quantity

	if player.coin < total_cost:
		return "Not enough coin (need %.1f, have %.1f)." % [total_cost, player.coin]

	# Execute.
	player.coin                    -= total_cost
	ss.market_inventory[good_id]    = maxf(available - quantity, 0.0)
	# Also reduce the bulk inventory proportionally.
	ss.inventory[good_id]           = maxf(
		float(ss.inventory.get(good_id, 0.0)) - quantity, 0.0)

	# Carrier inventory: add to carried_items (one entry per unit, capped at integer).
	for _i: int in range(int(quantity)):
		player.carried_items.append(good_id)

	# Skill XP for trading.
	player.award_skill_xp("trading", 0.01 * quantity)

	# Price impact: buying drives price up slightly.
	var impact_factor: float = 1.0 + (quantity / maxf(available, 1.0)) * 0.05
	ss.prices[good_id] = base_price * impact_factor

	return ""


## Sell `quantity` units of `good_id` from player's carried_items to settlement.
## Returns "" on success or an error string.
static func sell(
		player:   PersonState,
		ss:       SettlementState,
		good_id:  String,
		quantity: float) -> String:

	if quantity < MIN_SELL_QTY:
		return "Quantity too small."

	# Count how many of this good the player has.
	var held: int = player.carried_items.count(good_id)
	if float(held) < quantity:
		return "You don't have enough %s (have %d)." % [good_id, held]

	var base_price: float = float(ss.prices.get(good_id,
		ContentRegistry.get_content("good", good_id).get("base_value", 1.0)))
	var effective_price: float = base_price * _sell_multiplier(player)
	var total_revenue: float   = effective_price * quantity

	# Execute.
	player.coin += total_revenue
	for _i: int in range(int(quantity)):
		var idx: int = player.carried_items.find(good_id)
		if idx >= 0:
			player.carried_items.remove_at(idx)
	ss.market_inventory[good_id] = float(ss.market_inventory.get(good_id, 0.0)) + quantity
	ss.inventory[good_id]        = float(ss.inventory.get(good_id, 0.0)) + quantity

	player.award_skill_xp("trading", 0.01 * quantity)

	# Price impact: selling drives price down slightly.
	var supply: float    = float(ss.market_inventory.get(good_id, quantity))
	var impact_factor: float = 1.0 - (quantity / maxf(supply, 1.0)) * 0.05
	ss.prices[good_id] = maxf(base_price * impact_factor, 0.10)

	return ""


## Returns a list of goods available for purchase with prices and quantities.
## Format: Array[{good_id, quantity, unit_price, total_if_buy_1}]
static func market_listing(player: PersonState, ss: SettlementState) -> Array:
	var out: Array = []
	for good_id: String in ss.market_inventory.keys():
		var qty: float = float(ss.market_inventory.get(good_id, 0.0))
		if qty < MIN_BUY_QTY:
			continue
		var base: float = float(ss.prices.get(good_id, 1.0))
		out.append({
			"good_id":    good_id,
			"quantity":   qty,
			"unit_price": base * _buy_multiplier(player),
		})
	return out


# ── Helpers ───────────────────────────────────────────────────────────────────

## Effective buy price multiplier (skill reduces price).
static func _buy_multiplier(player: PersonState) -> float:
	var discount: float = minf(
		float(player.skill_level("trading")) * SKILL_MARGIN_PER_LEVEL,
		MAX_BUY_DISCOUNT)
	return 1.0 - discount


## Effective sell price multiplier (skill improves sell price).
static func _sell_multiplier(player: PersonState) -> float:
	var premium: float = minf(
		float(player.skill_level("trading")) * SKILL_MARGIN_PER_LEVEL,
		MAX_SELL_PREMIUM)
	return 1.0 + premium
