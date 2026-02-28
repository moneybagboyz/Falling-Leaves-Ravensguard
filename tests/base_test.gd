## BaseTest — shared assertion helpers for test suites.
## All test suite scripts should extend this class.
class_name BaseTest
extends RefCounted

## Set by TestRunner before each test method is called.
var _current_result: Object = null


func fail(message: String) -> void:
	if _current_result != null:
		_current_result.message = message


func assert_true(condition: bool, message: String = "Expected true") -> void:
	if not condition:
		fail(message)


func assert_false(condition: bool, message: String = "Expected false") -> void:
	if condition:
		fail("Expected false: " + message)


func assert_eq(a, b, message: String = "") -> void:
	if a != b:
		var msg := message if not message.is_empty() else "Expected %s == %s" % [str(a), str(b)]
		fail(msg)


func assert_ne(a, b, message: String = "") -> void:
	if a == b:
		fail(message if not message.is_empty() else "Expected %s != %s" % [str(a), str(b)])


func assert_not_null(value, message: String = "Expected non-null") -> void:
	if value == null:
		fail(message)


func assert_null(value, message: String = "Expected null") -> void:
	if value != null:
		fail(message)


func assert_gt(a, b, message: String = "") -> void:
	if not (a > b):
		fail(message if not message.is_empty() else "Expected %s > %s" % [str(a), str(b)])


func assert_has(dict_or_array, key, message: String = "") -> void:
	if dict_or_array is Dictionary:
		if not dict_or_array.has(key):
			fail(message if not message.is_empty() else "Dict missing key '%s'" % str(key))
	elif dict_or_array is Array:
		if not dict_or_array.has(key):
			fail(message if not message.is_empty() else "Array missing element '%s'" % str(key))
	else:
		fail("assert_has: argument is not a Dict or Array")
