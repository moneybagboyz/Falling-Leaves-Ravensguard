@warning_ignore("shadowed_global_identifier")
class_name EconomyManager
extends Node

const PricingSystem = preload("res://src/economy/PricingSystem.gd")
const TradeSystem = preload("res://src/economy/TradeSystem.gd")
const EquipmentSystem = preload("res://src/economy/EquipmentSystem.gd")
const ProductionSystem = preload("res://src/economy/ProductionSystem.gd")
const ConsumptionSystem = preload("res://src/economy/ConsumptionSystem.gd")

@warning_ignore("shadowed_global_identifier")
static func get_price(res_name, s_data):
	return PricingSystem.get_price(res_name, s_data)

@warning_ignore("shadowed_global_identifier")
static func get_buy_price(res_name, s_data):
	return PricingSystem.get_buy_price(res_name, s_data)

@warning_ignore("shadowed_global_identifier")
static func get_sell_price(res_name, s_data):
	return PricingSystem.get_sell_price(res_name, s_data)

@warning_ignore("shadowed_global_identifier")
static func get_item_price(type_key, mat_key, qual, is_commission := false):
	return PricingSystem.get_item_price(type_key, mat_key, qual, is_commission)

@warning_ignore("shadowed_global_identifier")
static func get_market_info(s_data, res):
	return PricingSystem.get_market_info(s_data, res)

@warning_ignore("shadowed_global_identifier")
static func buy_resource(s_data, res_name, amount, player_obj):
	return TradeSystem.buy_resource(s_data, res_name, amount, player_obj)

@warning_ignore("shadowed_global_identifier")
static func sell_resource(s_data, res_name, amount, player_obj):
	return TradeSystem.sell_resource(s_data, res_name, amount, player_obj)

static func update_trade_networks(gs):
	TradeSystem.update_trade_networks(gs)

@warning_ignore("shadowed_global_identifier")
static func resolve_caravan_trade(gs, caravan_obj):
	TradeSystem.resolve_caravan_trade(gs, caravan_obj)

@warning_ignore("shadowed_global_identifier")
static func recalculate_production(s_data, grid, resources, geology):
	ProductionSystem.recalculate_production(s_data, grid, resources, geology)

@warning_ignore("shadowed_global_identifier")
static func increment_prod(s_data, res_name, amount, geology = null):
	ProductionSystem.increment_prod(s_data, res_name, amount, geology)

@warning_ignore("shadowed_global_identifier")
static func process_daily_pulse(gs, s_data):
	# Invalidate price cache (prices change daily based on supply/demand)
	s_data.invalidate_cache("prices")
	
	# Refresh efficiency and housing caches (invalidated when buildings/unrest changes)
	s_data.cache_efficiency = s_data.get_workforce_efficiency()
	s_data.cache_housing_cap = s_data.get_housing_capacity()
	
	var efficiency = s_data.cache_efficiency
	
	# 1. Base Production from world resources (Spices, Ivory, etc.)
	for res in s_data.production_capacity:
		var amount = int(s_data.production_capacity[res] * efficiency)
		s_data.add_inventory(res, amount)
		GameState.track_production(res, amount)
	
	# 2. Labor Intensive Production (Food & Raw Materials)
	ProductionSystem._process_labor_pool(s_data, efficiency)
	
	# 3. Energy Pulses (Charcoal Burning)
	ProductionSystem._process_energy(s_data, efficiency)
	
	# CONSUMPTION BEFORE DUMPING: Let people eat before we throw away food
	ConsumptionSystem._process_consumption_and_growth(s_data)
	ConsumptionSystem._process_taxes(s_data) # Generate income to afford buildings
	ConsumptionSystem._process_storage_limits(s_data)
	ConsumptionSystem._process_settlement_logistics(gs, s_data)

# === EQUIPMENT SYSTEM DELEGATION ===

@warning_ignore("shadowed_global_identifier")
static func create_item(type_key, material_key, quality := "standard"):
	return EquipmentSystem.create_item(type_key, material_key, quality)

@warning_ignore("shadowed_global_identifier")
static func get_quality_rank(q) -> int:
	return EquipmentSystem.get_quality_rank(q)

@warning_ignore("shadowed_global_identifier")
static func get_kit_cost(player_obj, c_name, is_commission = false) -> int:
	return EquipmentSystem.get_kit_cost(player_obj, c_name, is_commission)

@warning_ignore("shadowed_global_identifier")
static func get_reequip_cost(player_obj, c_name) -> int:
	return EquipmentSystem.get_reequip_cost(player_obj, c_name)

@warning_ignore("shadowed_global_identifier")
static func fund_class_commissions(gs, s_pos, c_name):
	EquipmentSystem.fund_class_commissions(gs, s_pos, c_name)

@warning_ignore("shadowed_global_identifier")
static func create_class(gs, c_name, reqs):
	EquipmentSystem.create_class(gs, c_name, reqs)

@warning_ignore("shadowed_global_identifier")
static func assign_class(gs, unit_idx, c_name):
	EquipmentSystem.assign_class(gs, unit_idx, c_name)

@warning_ignore("shadowed_global_identifier")
static func check_readiness(player_obj, u_obj) -> Dictionary:
	return EquipmentSystem.check_readiness(player_obj, u_obj)

@warning_ignore("shadowed_global_identifier")
static func auto_equip_all(gs):
	EquipmentSystem.auto_equip_all(gs)

@warning_ignore("shadowed_global_identifier")
static func commission_items(gs, s_pos, type_key, mat_key, qual, count):
	EquipmentSystem.commission_items(gs, s_pos, type_key, mat_key, qual, count)

@warning_ignore("shadowed_global_identifier")
static func perform_equip(gs, u_obj, stash_idx):
	EquipmentSystem.perform_equip(gs, u_obj, stash_idx)

@warning_ignore("shadowed_global_identifier")
static func perform_unequip(gs, u_obj, slot, layer = ""):
	EquipmentSystem.perform_unequip(gs, u_obj, slot, layer)

# === CONSUMPTION SYSTEM DELEGATION ===

@warning_ignore("shadowed_global_identifier")
static func get_daily_tax(s_data) -> int:
	return ConsumptionSystem.get_daily_tax(s_data)

