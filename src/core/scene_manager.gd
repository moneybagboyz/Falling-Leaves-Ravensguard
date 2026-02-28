## SceneManager — stack-based scene router.
##
## Singleton autoload. All scene transitions must go through here so that
## push/pop semantics (e.g. WorldView → SettlementView → back) work correctly.
##
## Usage:
##   SceneManager.replace_scene("res://src/ui/world_view.tscn")
##   SceneManager.push_scene("res://src/ui/settlement_view.tscn", {settlement_id="…"})
##   SceneManager.pop_scene()   # in Esc handler of pushed scene
##
## Params: the incoming scene reads SceneManager.take_params() in its _ready().
extends Node

## Stack of file paths pushed BEFORE each push_scene() call.
## Popped on pop_scene() to return.
var _scene_stack: Array[String] = []

## Params dict delivered to the next scene. Cleared once consumed.
var _pending_params: Dictionary = {}


# ── Public API ────────────────────────────────────────────────────────────────

## Replace the current scene with no history entry.
## Use for top-level transitions that should not be undoable with Esc.
func replace_scene(path: String, params: Dictionary = {}) -> void:
	_pending_params = params
	get_tree().change_scene_to_file(path)


## Push a new scene onto the navigation stack.
## The current scene's path is recorded; pop_scene() returns to it.
func push_scene(path: String, params: Dictionary = {}) -> void:
	var current: Node = get_tree().current_scene
	if current != null and current.scene_file_path != "":
		_scene_stack.push_back(current.scene_file_path)
	_pending_params = params
	get_tree().change_scene_to_file(path)


## Return to the previous scene on the stack.
## Safe to call even if the stack is empty (logs a warning and does nothing).
func pop_scene(params: Dictionary = {}) -> void:
	if _scene_stack.is_empty():
		push_warning("SceneManager.pop_scene(): navigation stack is empty — no scene to return to.")
		return
	var previous: String = _scene_stack.pop_back()
	_pending_params = params
	get_tree().change_scene_to_file(previous)


## Read and consume the params dict delivered by the calling scene.
## Returns an empty Dictionary if no params were set.
## Always call this once in _ready() of scenes that accept params.
func take_params() -> Dictionary:
	var p: Dictionary = _pending_params.duplicate()
	_pending_params = {}
	return p


## Check whether params are waiting without consuming them.
func has_params() -> bool:
	return not _pending_params.is_empty()


## Depth of the current navigation stack (useful for showing/hiding Back buttons).
func stack_depth() -> int:
	return _scene_stack.size()


## Clear the stack (e.g. when returning to main menu from anywhere).
func clear_stack() -> void:
	_scene_stack.clear()
