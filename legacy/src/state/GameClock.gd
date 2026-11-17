class_name GameClock
extends RefCounted

# Time Tracking
var turn: int = 0
var hour: int = 0
var day: int = 1
var month: int = 1
var year: int = 1
var is_turbo: bool = false

# Event History
var event_log: Array[String] = []
var history: Array[Dictionary] = [] # {turn, day, month, year, text}
var monthly_ledger: Dictionary = {}

# Constants
const HOURS_PER_DAY = 24
const DAYS_PER_MONTH = 30
const MONTHS_PER_YEAR = 12
const MAX_LOG_SIZE = 300

signal log_updated
signal time_advanced(hour: int, day: int, month: int, year: int)
signal day_changed(day: int)
signal month_changed(month: int)
signal year_changed(year: int)

func _init():
	pass

func clear():
	"""Reset clock and logs"""
	turn = 0
	hour = 0
	day = 1
	month = 1
	year = 1
	is_turbo = false
	event_log.clear()
	history.clear()
	monthly_ledger.clear()

func add_log(message: String):
	"""Add message to event log"""
	event_log.append(message)
	if event_log.size() > MAX_LOG_SIZE:
		event_log.pop_front()
	emit_signal("log_updated")

func add_history(text: String):
	"""Add entry to historical record"""
	history.append({
		"turn": turn,
		"day": day,
		"month": month,
		"year": year,
		"text": text
	})

func advance_time(hours: int = 1):
	"""Advance time by specified hours"""
	var old_day = day
	var old_month = month
	var old_year = year
	
	hour += hours
	turn += 1
	
	# Handle day rollover
	while hour >= HOURS_PER_DAY:
		hour -= HOURS_PER_DAY
		day += 1
		
		if day > DAYS_PER_MONTH:
			day = 1
			month += 1
			
			if month > MONTHS_PER_YEAR:
				month = 1
				year += 1
	
	# Emit signals for state changes
	emit_signal("time_advanced", hour, day, month, year)
	
	if day != old_day:
		emit_signal("day_changed", day)
	
	if month != old_month:
		emit_signal("month_changed", month)
		monthly_ledger.clear() # Reset monthly stats
	
	if year != old_year:
		emit_signal("year_changed", year)

func get_date_string() -> String:
	"""Get formatted date string"""
	return "Day %d, Month %d, Year %d" % [day, month, year]

func get_time_string() -> String:
	"""Get formatted time string"""
	return "%02d:00" % hour

func get_full_datetime() -> String:
	"""Get full date and time string"""
	return "%s @ %s" % [get_date_string(), get_time_string()]

func get_season() -> String:
	"""Get current season based on month"""
	match month:
		1, 2, 3: return "Winter"
		4, 5, 6: return "Spring"
		7, 8, 9: return "Summer"
		10, 11, 12: return "Autumn"
		_: return "Unknown"

func is_night() -> bool:
	"""Check if it's nighttime"""
	return hour >= 20 or hour < 6

func is_day() -> bool:
	"""Check if it's daytime"""
	return not is_night()

func get_turn_count() -> int:
	"""Get total turn count"""
	return turn

func get_days_elapsed() -> int:
	"""Get total days since start"""
	return (year - 1) * MONTHS_PER_YEAR * DAYS_PER_MONTH + (month - 1) * DAYS_PER_MONTH + (day - 1)
