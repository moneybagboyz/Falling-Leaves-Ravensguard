extends Object
class_name NamesData

static var _names_cache = null

static func get_names_data() -> Dictionary:
	if _names_cache == null:
		var file = FileAccess.open("res://data/names.json", FileAccess.READ)
		if file:
			var json_string = file.get_as_text()
			file.close()
			var json = JSON.new()
			var error = json.parse(json_string)
			if error == OK:
				_names_cache = json.data
			else:
				push_error("Failed to parse names.json: " + json.get_error_message())
				_names_cache = {"months": [], "first_names": [], "last_names": []}
		else:
			push_error("Failed to load names.json")
			_names_cache = {"months": [], "first_names": [], "last_names": []}
	return _names_cache

static func get_month_names() -> Array:
	return get_names_data().get("months", [])

static func get_first_names() -> Array:
	return get_names_data().get("first_names", [])

static func get_last_names() -> Array:
	return get_names_data().get("last_names", [])

static func get_random_name(rng: RandomNumberGenerator) -> String:
	var first = get_first_names()
	var last = get_last_names()
	if first.is_empty() or last.is_empty():
		return "Unknown"
	return first[rng.randi() % first.size()] + " " + last[rng.randi() % last.size()]

static func get_month_name(month_index: int) -> String:
	var months = get_month_names()
	if month_index < 0 or month_index >= months.size():
		return "Unknown"
	return months[month_index]
