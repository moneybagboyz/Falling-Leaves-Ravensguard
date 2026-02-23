extends Node

## GameClock — global singleton (autoload).
## The only source of time in the game. All simulation is driven by these signals.
## Turn = 1 hour of in-game time.
##
## Register in Project Settings → AutoLoad as "GameClock".

signal hourly_pulse(turn: int)
signal daily_pulse(turn: int)
signal weekly_pulse(turn: int)
signal monthly_pulse(turn: int)

## Current turn counter. Never decremented or reset.
var turn: int = 0

## Wall-clock speed: how many seconds of real time per in-game hour.
## 1.0 = 1 sec/hour (fast); 0.0 = paused.
var time_scale: float = 1.0

## Whether the clock is paused by the player (separate from time_scale).
var paused: bool = false

var _accumulator: float = 0.0


func _process(delta: float) -> void:
	if paused or time_scale <= 0.0:
		return
	_accumulator += delta
	while _accumulator >= time_scale:
		_accumulator -= time_scale
		advance(1)


## Advance the clock by `hours` in-game hours, firing all signals synchronously.
## Called by _process automatically, but can also be called manually (e.g. from
## a "Wait 8 hours" player action or fast-forward button).
func advance(hours: int = 1) -> void:
	for _i in range(hours):
		turn += 1
		emit_signal("hourly_pulse", turn)
		if turn % 24 == 0:
			emit_signal("daily_pulse", turn)
		if turn % 168 == 0:   # 24 × 7
			emit_signal("weekly_pulse", turn)
		if turn % 720 == 0:   # 24 × 30
			emit_signal("monthly_pulse", turn)


## Convenience: current day number (starting at 1).
func day() -> int:
	return turn / 24 + 1


## Convenience: current hour within the day (0..23).
func hour_of_day() -> int:
	return turn % 24


## Convenience: current week number.
func week() -> int:
	return turn / 168 + 1


## Human-readable timestamp string.
func timestamp() -> String:
	return "Day %d, %02d:00" % [day(), hour_of_day()]
