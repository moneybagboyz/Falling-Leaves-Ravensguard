class_name UIConstants
extends RefCounted

## Centralized UI styling constants for consistent visual design

# ============================================================================
# COLOR PALETTE
# ============================================================================

const Colors = {
	# Panel borders (subtle color coding)
	"MAP_BORDER": Color(0.3, 0.3, 0.4),
	"INFO_BORDER": Color(0.2, 0.4, 0.3),
	"LOG_BORDER": Color(0.4, 0.3, 0.2),
	
	# Cursor states
	"CURSOR_HOVER": Color(1, 1, 1, 0.3),
	"CURSOR_SELECT": Color(1, 1, 0, 0.5),
	"CURSOR_INTERACT": Color(0.3, 1, 0.3, 0.5),
	
	# Text colors
	"TEXT_SELECTED": "#ffff00",      # Yellow
	"TEXT_INACTIVE": "#808080",      # Gray
	"TEXT_DISABLED": "#505050",      # Dark gray
	"TEXT_HEADER": "#ffffff",        # White
	"TEXT_HIGHLIGHT": "#00ffff",     # Cyan
	"TEXT_WARNING": "#ff8800",       # Orange
	"TEXT_ERROR": "#ff0000",         # Red
	"TEXT_SUCCESS": "#00ff00",       # Green
	
	# Background colors (for headers/sections)
	"BG_HEADER": "#1a3a4a",          # Dark blue
	"BG_TAB_ACTIVE": "#3a5a6a",      # Medium blue
	"BG_SECTION": "#2a2a2a",         # Dark gray
	"BG_RECRUIT": "#2a4a2a",         # Dark green
	"BG_TRADE": "#2a4a4a",           # Dark cyan
	"BG_SQUARE": "#4a3a2a",          # Dark brown
	"BG_OFFICE": "#3a2a4a",          # Dark purple
	"BG_WORLD": "#2a3a4a",           # Dark blue-gray
	"BG_MARKET_INV": "#1a2a1a",      # Very dark green
	"BG_MARKET_SHOP": "#2a1a1a",     # Very dark red
	"BG_WARNING": "#4a2a2a",         # Dark red
	"BG_CODEX": "#1a2a3a",           # Dark navy
	"BG_DIALOGUE": "#1a3a4a",        # Dark teal
}

# ============================================================================
# UI SYMBOLS (ASCII-friendly alternatives to emojis)
# ============================================================================

const Symbols = {
	# General UI
	"MENU": "≡",
	"ARROW_RIGHT": "►",
	"ARROW_UP": "▲",
	"ARROW_DOWN": "▼",
	"ARROW_LEFT": "◄",
	"SEPARATOR": "─",
	"BULLET": "•",
	"POINTER": ">",
	"CHECKBOX_ON": "[X]",
	"CHECKBOX_OFF": "[ ]",
	
	# Categories
	"PERSON": "@",
	"PEOPLE": "@@",
	"BUILDING": "[]",
	"CASTLE": "#",
	"COIN": "$",
	"WEIGHT": "kg",
	"DOCUMENT": "~",
	"BOOK": "B",
	"SCROLL": "~",
	"WARNING": "!",
	"INFO": "i",
	"QUEST": "?",
	
	# Resources
	"WHEAT": "%",
	"WOOD": "|",
	"STONE": "o",
	"METAL": "*",
	"BARREL": "U",
	"BOX": "[]",
	
	# Actions
	"TRADE": "$",
	"RECRUIT": "R",
	"WEAPON": "/",
	"SHIELD": ")",
	"CHAIN": "#",
}

# ============================================================================
# UI TEXT TEMPLATES
# ============================================================================

const Headers = {
	"MANAGEMENT": "[center][bgcolor={bg}][b] {symbol} COMMODORE MANAGEMENT {symbol} [/b][/bgcolor][/center]\n",
	"SECTION": "[bgcolor={bg}][b] {title} [/b][/bgcolor]",
	"SUBSECTION": "[bgcolor={bg}][b] {title} [/b][/bgcolor]\n",
	"SEPARATOR": "[center][color={color}]{line}[/color][/center]\n",
}

const Controls = {
	"BUY_SELL": "[center][bgcolor=#2a2a2a][color=green] {up} [/color] Buy [color=red] {down} [/color] Sell [color=gray]({note})[/color][/bgcolor][/center]",
	"NAVIGATION": "[center][bgcolor=#2a2a2a][color=cyan] {left} {right} [/color] Switch Focus [color=yellow] ENTER [/color] {action}[/bgcolor][/center]",
	"ACTION": "[center][bgcolor=#2a2a2a][color=yellow] {key} [/color] {action}[/bgcolor][/center]",
	"MULTI_ACTION": "[center][bgcolor=#2a2a2a]{actions}[/bgcolor][/center]",
}

# ============================================================================
# HELPER FUNCTIONS
# ============================================================================

static func format_header(type: String, params: Dictionary = {}) -> String:
	"""Format a header with given parameters"""
	if not Headers.has(type):
		return ""
	
	var template = Headers[type]
	for key in params:
		template = template.replace("{" + key + "}", str(params[key]))
	return template

static func format_control(type: String, params: Dictionary = {}) -> String:
	"""Format a control hint with given parameters"""
	if not Controls.has(type):
		return ""
	
	var template = Controls[type]
	for key in params:
		template = template.replace("{" + key + "}", str(params[key]))
	return template

static func get_separator(length: int = 60, color: String = "#505050") -> String:
	"""Generate a horizontal separator line"""
	return "[center][color=%s]%s[/color][/center]\n" % [color, Symbols.SEPARATOR.repeat(length)]

static func highlight_tab(tab_name: String, is_active: bool) -> String:
	"""Format a tab name based on active state"""
	if is_active:
		return "[bgcolor=%s][b] %s [/b][/bgcolor] " % [Colors.BG_TAB_ACTIVE, tab_name]
	else:
		return "[color=%s]%s[/color] " % [Colors.TEXT_INACTIVE, tab_name]

static func format_selection(text: String, is_selected: bool, prefix: String = "") -> String:
	"""Format a selectable item"""
	var arrow = Symbols.ARROW_RIGHT if is_selected else "  "
	var color = Colors.TEXT_SELECTED if is_selected else Colors.TEXT_INACTIVE
	var display_prefix = prefix if prefix != "" else arrow
	return "[color=%s]%s %s[/color]\n" % [color, display_prefix, text]

static func format_stat_color(current: float, max_val: float, thresholds: Dictionary = {}) -> String:
	"""Get color for a stat based on value (default: green > yellow > red)"""
	var ratio = current / max_val if max_val > 0 else 0
	var high = thresholds.get("high", 0.8)
	var low = thresholds.get("low", 0.3)
	
	if ratio >= high:
		return Colors.TEXT_SUCCESS
	elif ratio >= low:
		return Colors.TEXT_WARNING
	else:
		return Colors.TEXT_ERROR

static func format_money(amount: int, low_threshold: int = 100, critical_threshold: int = 20) -> String:
	"""Format money with appropriate color coding"""
	var color = "yellow"
	if amount < critical_threshold:
		color = "red"
	elif amount < low_threshold:
		color = "orange"
	return "[color=%s]%d[/color]" % [color, amount]

static func format_weight(current: float, max_weight: float) -> String:
	"""Format weight with color based on capacity"""
	var color = "white"
	if current >= max_weight:
		color = "red"
	elif current > max_weight * 0.8:
		color = "orange"
	return "[color=%s]%d/%d kg[/color]" % [color, int(current), int(max_weight)]
