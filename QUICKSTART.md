# 🚀 Quick Start - Testing New Architecture

## Step 1: Run Automated Tests (2 minutes)

### In Godot Editor:
1. **Open** this project in Godot 4.x
2. Press **F6** (Run Current Scene)
3. **Select:** `test_new_architecture.tscn`
4. **Watch** the console output

### Expected Result:
```
✓ ALL TESTS PASSED - New architecture is functional!
```

---

## Step 2: Deploy (If Tests Pass)

### Option A: Using Batch File (Easiest)
```cmd
deploy_architecture.bat
```
Then select:
- **Option 2**: Create backup
- **Option 3**: Deploy new architecture

### Option B: Manual Commands
```powershell
# Backup
Copy-Item "GameState.gd" "GameState_OLD.gd"

# Deploy
Copy-Item "src\core\GameState_New.gd" "GameState.gd" -Force
```

---

## Step 3: Test Full Game (30 minutes)

### In Godot Editor:
1. Press **F5** (Run Main Scene)
2. Test these features:

**Essential Tests:**
- ✅ World generation works
- ✅ Can move on overworld
- ✅ Time advances (T key)
- ✅ Can enter settlements (E)
- ✅ Battle system works
- ✅ Trading works

**Check Console:**
- ❌ No red errors
- ⚠️ Warnings are OK

---

## If Something Breaks

### Quick Rollback:
```cmd
deploy_architecture.bat
```
Select **Option 4**: Rollback

### Or Manually:
```powershell
Copy-Item "GameState_OLD.gd" "GameState.gd" -Force
```

---

## Files Overview

**Testing:**
- `test_new_architecture.tscn` - Automated test scene
- `test_new_architecture.gd` - Test script
- `TESTING_GUIDE.md` - Detailed testing checklist

**Deployment:**
- `deploy_architecture.bat` - Easy deployment menu
- `deploy_architecture.ps1` - PowerShell version

**Documentation:**
- `TESTING_README.md` - This file
- `REFACTORING_SUMMARY.md` - What was changed
- `docs/Architecture_Refactoring_Phase3.md` - Technical details

---

## What Changed?

### Before:
- **GameState.gd**: 1,090 lines (monolithic)
- **Main.gd**: 2,521 lines with 211-line render method

### After:
- **5 State Modules**: WorldState, EntityRegistry, PlayerState, GameClock, StateManager
- **5 UI Renderers**: Clean separation of rendering logic
- **GameState.gd**: Now a facade (100% compatible)
- **Main.gd**: Render method reduced to 25 lines (-88%)

---

## Why This Is Better

✅ **Modular**: Easy to find and modify code  
✅ **Testable**: Each module can be tested independently  
✅ **Maintainable**: Clear responsibilities  
✅ **Backward Compatible**: All existing code works  

---

## Need Help?

**Check:**
1. Console output for specific errors
2. `TESTING_GUIDE.md` for detailed checklist
3. `REFACTORING_SUMMARY.md` for architecture overview

**Common Issues:**
- "Can't find module" → Check file paths in src/state/ and src/ui/
- "Property not found" → Check GameState_New.gd property forwarding
- Game won't start → Rollback and check console errors

---

**Ready? Open Godot and press F6!** 🎮
