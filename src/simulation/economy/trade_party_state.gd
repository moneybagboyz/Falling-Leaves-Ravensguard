## TradePartyState — plain data class for one active trade party.
##
## A trade party carries cargo from an origin settlement to a destination
## settlement along a pre-built route path. PartyCore advances path_idx
## each movement tick; on arrival the cargo is transferred to destination.
class_name TradePartyState
extends RefCounted

var party_id:             String  = ""
var origin_id:            String  = ""  # settlement_id of spawning settlement
var dest_id:              String  = ""  # settlement_id of destination
var cargo:                Dictionary = {}  # good_id → float quantity
var path:                 Array   = []   # Array of [x, y] int pairs (JSON-safe)
var path_idx:             int     = 0    # current index along path
var speed_tiles_per_tick: float   = 2.0  # tile steps advanced per movement tick
var ticks_en_route:       int     = 0    # total ticks spent travelling


func to_dict() -> Dictionary:
	return {
		"party_id":              party_id,
		"origin_id":             origin_id,
		"dest_id":               dest_id,
		"cargo":                 cargo.duplicate(),
		"path":                  path.duplicate(),
		"path_idx":              path_idx,
		"speed_tiles_per_tick":  speed_tiles_per_tick,
		"ticks_en_route":        ticks_en_route,
	}


static func from_dict(d: Dictionary) -> TradePartyState:
	var tp := TradePartyState.new()
	tp.party_id             = d.get("party_id",             "")
	tp.origin_id            = d.get("origin_id",            "")
	tp.dest_id              = d.get("dest_id",              "")
	tp.cargo                = d.get("cargo",                {})
	tp.path                 = d.get("path",                 [])
	tp.path_idx             = d.get("path_idx",             0)
	tp.speed_tiles_per_tick = d.get("speed_tiles_per_tick", 2.0)
	tp.ticks_en_route       = d.get("ticks_en_route",       0)
	return tp
