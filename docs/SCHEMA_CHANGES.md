# Schema Changes & Migration Guide

**Last updated:** 2026-03-09  
**Current schema version:** 0.2.0

## Overview

Ravensguard uses versioned save files with automatic migration support. Every change to the save format requires a corresponding migration to maintain save compatibility across game versions.

## Rules (Non-Negotiable)

### Rule 1: Version Bumping

**When to bump the schema version:**
- Adding/removing fields from `WorldState`, `SettlementState`, or `PersonState`
- Changing the meaning of existing fields
- Restructuring nested data (e.g., changing `Dictionary` to typed class)
- Modifying serialization format

**How to bump:**
1. Update `SaveManager.CURRENT_SCHEMA_VERSION` (e.g., `"0.2.0"` → `"0.3.0"`)
2. Register a migration in `Bootstrap._register_migrations()` before the change ships

**Do NOT bump for:**
- Adding optional fields with `.get("field", default)` pattern that have safe defaults
- Changes to runtime-only state that never serializes
- Pure code refactoring that doesn't touch persisted data

### Rule 2: Migration Registration

Every version bump MUST have a corresponding migration function:

```gdscript
MigrationRunner.register_migration("0.2.0", "0.3.0", _migrate_0_2_0_to_0_3_0)
```

**Migration function signature:**
```gdscript
static func _migrate_0_2_0_to_0_3_0(data: Dictionary) -> Dictionary:
    # Mutate data in-place or create new dict
    # MUST return the modified data
    return data
```

**Order matters:** Migrations are chained. Register them in chronological order in `Bootstrap._register_migrations()`.

### Rule 3: Forward Compatibility

**Use `.get()` with defaults everywhere:**
```gdscript
# GOOD — safe if field is missing
var value: float = data.get("new_field", 0.0)

# BAD — crashes on old saves
var value: float = data["new_field"]
```

**Test with old saves:** Before releasing, load a save created with the previous version and verify migration succeeds.

## Migration vs. Data Repair

**Migrations** are for schema changes:
- Run once per save at load time
- Tied to version numbers
- Registered in `MigrationRunner`

**Data repair guards** are for idempotent fixes:
- Run on every load
- Handle legacy saves AND future edge cases
- Example: `NpcPoolManager.ensure_spawned()` in `Bootstrap.continue_game()`

Use data repair guards for:
- Backfilling data that was missing due to bugs
- Ensuring invariants (e.g., "every settlement must have NPCs")
- Fixes that are safe to run multiple times

Document data repair guards with a comment explaining why they're permanent.

## Current Migration History

### 0.1.0 → 0.2.0
**Date:** 2026-03-02  
**Reason:** Road tile schema change — old saves have roads without `road_dirs` field.  
**Action:** Clear cached `region_grids` to force regeneration with new schema.  
**Migration:** `Bootstrap._migrate_0_1_0_to_0_2_0()`

## Adding a New Migration (Step-by-Step)

### Example: Adding a new field to SettlementState

**1. Write the migration function in `bootstrap.gd`:**
```gdscript
static func _migrate_0_2_0_to_0_3_0(data: Dictionary) -> Dictionary:
    var ws_data: Dictionary = data.get("world_state", {})
    var settlements: Dictionary = ws_data.get("settlements", {})
    
    for sid in settlements:
        var ss: Dictionary = settlements[sid]
        # Add new field with default value if missing
        if not ss.has("new_field"):
            ss["new_field"] = default_value
    
    return data
```

**2. Register the migration in `Bootstrap._register_migrations()`:**
```gdscript
func _register_migrations() -> void:
    MigrationRunner.register_migration("0.1.0", "0.2.0", _migrate_0_1_0_to_0_2_0)
    MigrationRunner.register_migration("0.2.0", "0.3.0", _migrate_0_2_0_to_0_3_0)  # NEW
```

**3. Bump the schema version in `save_manager.gd`:**
```gdscript
const CURRENT_SCHEMA_VERSION := "0.3.0"  # was "0.2.0"
```

**4. Update this document with the migration details.**

**5. Test:**
- Load a 0.2.0 save — should migrate automatically
- Create a new game — should use 0.3.0 directly
- Load a 0.1.0 save — should chain through both migrations

## Schema Version Numbering

Use semantic versioning: `MAJOR.MINOR.PATCH`

- **MAJOR:** Complete save format overhaul (e.g., switching from JSON to binary)
- **MINOR:** Breaking changes requiring migration (field additions, restructuring)
- **PATCH:** Forward-compatible changes (new optional fields with defaults)

Currently all changes are MINOR bumps (0.1.0 → 0.2.0 → 0.3.0).

## Debugging Migration Failures

**If a migration fails:**
1. Check the console — `MigrationRunner` logs each migration step
2. Verify the migration function returns the modified `data` Dictionary
3. Confirm the from/to versions match the registered migration
4. Test with a minimal save file that reproduces the issue

**Common mistakes:**
- Forgetting to return `data` from the migration function
- Mutating a nested dict without reassigning to parent (use `.duplicate(true)` if needed)
- Registering migrations out of order

## CI Checks (Future)

When CI is set up, add:
- Test that loads saves from all previous versions
- Lint check: schema version changed without migration registration → fail build
- Migration smoke test: verify all registered migrations execute without errors

## Questions?

- **"Can I skip a migration if I'm sure no one has that version?"**  
  No. Once a version is released (even in dev), migrations must exist. The migration runner chains them automatically.

- **"Should I serialize `working_character_ids` or rebuild on load?"**  
  Rebuild on load is safer — indices are performance optimizations, not source of truth.

- **"What about content data migrations (e.g., renaming a good ID)?"**  
  Content changes are separate — use `content_version` fields in JSON and validate at load time with a cross-reference checker.

---

**In case of emergency:** If a broken migration ships, release a hotfix with a new migration that fixes the damage and bumps the version again. Never delete a registered migration.
