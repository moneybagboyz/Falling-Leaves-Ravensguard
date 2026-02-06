class_name UIFormatting
extends RefCounted

# BBCode formatting utilities with caching
static var _color_hex_cache := {}

# Color conversion to BBCode hex
static func color_to_hex(c: Color) -> String:
	if _color_hex_cache.has(c):
		return _color_hex_cache[c]
	var h := "#" + c.to_html(false)
	_color_hex_cache[c] = h
	return h

# BBCode tag wrappers
static func color_tag(text: String, color: Variant) -> String:
	var hex := _to_hex(color)
	return "[color=%s]%s[/color]" % [hex, text]

static func bgcolor_tag(text: String, color: Variant) -> String:
	var hex := _to_hex(color)
	return "[bgcolor=%s]%s[/bgcolor]" % [hex, text]

static func bold(text: String) -> String:
	return "[b]%s[/b]" % text

static func center(text: String) -> String:
	return "[center]%s[/center]" % text

static func table(columns: int, data: Array) -> String:
	var parts := PackedStringArray()
	parts.append("[table=%d]" % columns)
	for row in data:
		if row is Array:
			for cell in row:
				parts.append("[cell]%s[/cell]" % str(cell))
		else:
			parts.append("[cell]%s[/cell]" % str(row))
	parts.append("[/table]")
	return "".join(parts)

static func _to_hex(color: Variant) -> String:
	if color is String:
		return color
	if color is Color:
		return color_to_hex(color)
	return "white"

# Grid mode wrapper (for backward compatibility)
static func wrap_grid(gs, terrain_color: Color, content: String) -> String:
	# Note: Grid mode is deprecated with viewport rendering
	# Keeping for text-mode fallback compatibility
	if gs.render_mode == "grid":
		return bgcolor_tag(content, terrain_color)
	return content

# Join array of strings with newlines
static func join_lines(lines: Variant) -> String:
	if lines is PackedStringArray or lines is Array:
		return "\n".join(lines)
	return str(lines)

# Build a simple menu with cursor
static func build_menu(title: String, options: Array, selected_idx: int, cursor: String = " > ") -> String:
	var parts := PackedStringArray()
	parts.append(center(bold(title)))
	parts.append("\n\n")
	
	for i in range(options.size()):
		var prefix := cursor if i == selected_idx else "   "
		var suffix := cursor.reverse() if i == selected_idx else "   "
		parts.append(prefix + str(options[i]) + suffix + "\n")
	
	return "".join(parts)

# Build a config/settings screen
static func build_config(title: String, config: Dictionary, selected_idx: int, show_generate: bool = true) -> String:
	var parts := PackedStringArray()
	parts.append(center(bold(title)))
	parts.append("\n\n")
	
	var keys := config.keys()
	for i in range(keys.size()):
		var k := keys[i]
		var val := config[k]
		var prefix := " > " if i == selected_idx else "   "
		parts.append("%s%s: %s\n" % [prefix, k.capitalize(), str(val)])
	
	if show_generate:
		parts.append("\n")
		var gen_prefix := " > " if selected_idx == keys.size() else "   "
		parts.append("%s[ GENERATE ]\n" % gen_prefix)
	
	return "".join(parts)

# Clear caches
static func clear_cache() -> void:
	_color_hex_cache.clear()
