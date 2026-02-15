# Testing Guide - New Architecture

## Quick Test (5 minutes)

### Step 1: Run Automated Tests
1. Open Godot project
2. In Godot editor, press **F6** (Run Scene)
3. Select `test_new_architecture.tscn`
4. Check console output for test results

**Expected Output:**
```
=== TESTING NEW ARCHITECTURE ===

Test 1: Module Instantiation
  ✓ WorldState created
  ✓ EntityRegistry created
  ✓ PlayerState created
  ✓ GameClock created
  ✓ StateManager created

Test 2: GameState_New Facade
  ✓ GameState_New modules initialized

Test 3: Property Forwarding
  ✓ World property forwarding works
  ✓ Clock property forwarding works

Test 4: State Transitions
  ✓ Valid transition: MENU -> WORLD_CREATION
  ✓ Invalid transition rejected: WORLD_CREATION -> BATTLE

Test 5: Module Methods
  ✓ GameClock.advance_time() works
  ✓ GameClock.add_log() works
  ✓ WorldState.get_tile() works
  ✓ EntityRegistry.get_faction_by_id() works

=== TEST RESULTS ===
✓ ALL TESTS PASSED - New architecture is functional!
```

---

## Full Game Test (30 minutes)

### Prerequisites
⚠️ **IMPORTANT**: Only proceed if automated tests pass!

### Step 2: Back Up Current GameState
```powershell
# In VS Code terminal:
Copy-Item "GameState.gd" "GameState_OLD.gd"
```

### Step 3: Activate New GameState
```powershell
# Replace old with new:
Copy-Item "src/core/GameState_New.gd" "GameState.gd" -Force
```

### Step 4: Test All Game Features

Run Main.tscn (F5) and test each feature:

#### ✅ Main Menu
- [ ] Menu displays correctly
- [ ] Can navigate options

#### ✅ World Creation
- [ ] Can configure world settings
- [ ] World generates without errors
- [ ] Returns to menu if cancelled

#### ✅ Character Creation
- [ ] Can create character
- [ ] Stats display correctly
- [ ] Can start game

#### ✅ Overworld
- [ ] Map displays correctly
- [ ] Can move with WASD
- [ ] Settlements visible
- [ ] Armies visible
- [ ] Time advances (T key)
- [ ] Can enter settlements (E key)

#### ✅ Settlement/City View
- [ ] City displays
- [ ] Can view markets
- [ ] Can trade/buy/sell
- [ ] Can recruit units
- [ ] Can exit (ESC)

#### ✅ Battle System
- [ ] Combat initiates when encountering enemy
- [ ] Battle UI displays
- [ ] Can attack/defend
- [ ] Turn processing works
- [ ] Battle resolves correctly

#### ✅ Management
- [ ] Party screen (P key)
- [ ] Character stats display
- [ ] Inventory works
- [ ] Equipment system works

#### ✅ Faction/Diplomacy
- [ ] Faction info displays
- [ ] Relations update
- [ ] Campaigns work

#### ✅ Economy
- [ ] Trade contracts execute
- [ ] Prices update
- [ ] Resources produce/consume
- [ ] Caravans move

---

## If Tests Fail

### Rollback Process
```powershell
# Restore old GameState:
Copy-Item "GameState_OLD.gd" "GameState.gd" -Force

# Check for errors:
# In Godot, check Output tab for error messages
```

### Common Issues

**Issue**: "Invalid get index 'world' (on base: 'GDScript')"
- **Cause**: GameState_New._ready() didn't run
- **Fix**: Ensure GameState is added to scene tree

**Issue**: "Can't convert argument 1 from Nil to String"
- **Cause**: Module method expects different parameter
- **Fix**: Check method signatures in new modules

**Issue**: "Identifier not found: settlements"
- **Cause**: Property forwarding missing
- **Fix**: Check GameState_New.gd has property getter

---

## Performance Benchmarks

Test these scenarios and compare before/after:

1. **World Generation Time**: Should be identical
2. **Turn Processing**: Should be identical (±5ms)
3. **Battle Turns**: Should be identical
4. **UI Rendering**: Should be identical

If performance degrades >10%, investigate property forwarding overhead.

---

## Debugging Tips

### Enable Verbose Logging
Add to Main.gd `_ready()`:
```gdscript
GameState.state_manager.state_changed.connect(_on_state_changed)

func _on_state_changed(old_state, new_state):
    print("State: %s -> %s" % [old_state, new_state])
```

### Check Module Initialization
Add to GameState_New.gd `_ready()`:
```gdscript
print("GameState modules:")
print("  world: ", world != null)
print("  entities: ", entities != null)
print("  player_state: ", player_state != null)
print("  clock: ", clock != null)
print("  state_manager: ", state_manager != null)
```

### Monitor Property Access
Temporarily add prints to getters:
```gdscript
var grid: Array:
    get:
        print("Accessing grid via forwarding")
        return world.grid
```

---

## Success Criteria

✅ All automated tests pass  
✅ All manual features work  
✅ No console errors  
✅ No performance degradation  
✅ Save/load works (if implemented)

When all criteria met:
1. Delete GameState_OLD.gd
2. Update documentation
3. Commit changes

---

## Notes

- New architecture is 100% backward compatible
- Property forwarding is transparent to existing code
- All subsystems (Battle, Economy, etc.) continue to work
- UI renderers are independent of state modules
