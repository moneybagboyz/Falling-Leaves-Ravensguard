## EventQueue — global fire-and-forget event bus.
##
## Modules push events during a tick; listeners are notified when the
## TickScheduler calls flush() at the END of the tick (after all phases).
## This prevents order-of-registration from affecting simulation logic.
##
## Modules must NOT call each other directly — use this bus or StateDeltas.
extends Node

## Registered listeners: event_type -> Array[Callable]
var _listeners: Dictionary = {}

## Events queued during the current tick, dispatched on flush().
var _queue: Array[Dictionary] = []


# ---------------------------------------------------------------------------
# Subscription
# ---------------------------------------------------------------------------

func subscribe(event_type: String, callable: Callable) -> void:
	if not _listeners.has(event_type):
		_listeners[event_type] = []
	_listeners[event_type].append(callable)


func unsubscribe(event_type: String, callable: Callable) -> void:
	if not _listeners.has(event_type):
		return
	var arr: Array = _listeners[event_type]
	var idx := arr.find(callable)
	if idx >= 0:
		arr.remove_at(idx)


func has_subscribers(event_type: String) -> bool:
	return _listeners.has(event_type) and not _listeners[event_type].is_empty()


# ---------------------------------------------------------------------------
# Publishing
# ---------------------------------------------------------------------------

## Queue an event to be dispatched at end-of-tick.
## payload must be a plain Dictionary (will be passed by ref to listeners).
func push(event_type: String, payload: Dictionary = {}) -> void:
	_queue.append({"type": event_type, "payload": payload, "tick": SimulationClock.get_tick()})


## Dispatch all queued events to their registered listeners and clear the queue.
## Called automatically by TickScheduler after all phases complete.
func flush() -> void:
	if _queue.is_empty():
		return
	var batch := _queue.duplicate()
	_queue.clear()
	for event: Dictionary in batch:
		var listeners: Array = _listeners.get(event["type"], [])
		for listener: Callable in listeners:
			listener.call(event["payload"])


## Drain and discard all queued events without dispatching (used in tests / resets).
func clear() -> void:
	_queue.clear()


func queued_count() -> int:
	return _queue.size()
