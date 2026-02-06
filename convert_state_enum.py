#!/usr/bin/env python3
"""
Script to convert string-based state to enum in Main.gd
"""
import re

# State string to enum mapping
STATE_MAP = {
    '"menu"': 'GameEnums.GameMode.MENU',
    '"loading"': 'GameEnums.GameMode.LOADING',
    '"world_creation"': 'GameEnums.GameMode.WORLD_CREATION',
    '"world_preview"': 'GameEnums.GameMode.WORLD_PREVIEW',
    '"character_creation"': 'GameEnums.GameMode.CHARACTER_CREATION',
    '"play_select"': 'GameEnums.GameMode.PLAY_SELECT',
    '"location_select"': 'GameEnums.GameMode.PLAY_SELECT',  # Alias
    '"overworld"': 'GameEnums.GameMode.OVERWORLD',
    '"battle"': 'GameEnums.GameMode.BATTLE',
    '"battle_config"': 'GameEnums.GameMode.BATTLE_CONFIG',
    '"management"': 'GameEnums.GameMode.MANAGEMENT',
    '"dungeon"': 'GameEnums.GameMode.DUNGEON',
    '"dialogue"': 'GameEnums.GameMode.DIALOGUE',
    '"codex"': 'GameEnums.GameMode.CODEX',
    '"city"': 'GameEnums.GameMode.CITY',
    '"city_studio"': 'GameEnums.GameMode.CITY',  # Alias
    '"region"': 'GameEnums.GameMode.REGION',
    '"world_map"': 'GameEnums.GameMode.WORLD_PREVIEW',  # Alias
    '"history"': 'GameEnums.GameMode.OVERWORLD',  # Temporary alias
    '"party_info"': 'GameEnums.GameMode.MANAGEMENT',  # Temporary alias
}

def convert_file(filepath):
    with open(filepath, 'r', encoding='utf-8') as f:
        content = f.read()
    
    original = content
    
    # Replace state comparisons: state == "menu"
    for string_state, enum_state in STATE_MAP.items():
        content = re.sub(
            r'\bstate\s*==\s*' + re.escape(string_state),
            f'state == {enum_state}',
            content
        )
    
    # Replace state assignments: state = "menu"
    for string_state, enum_state in STATE_MAP.items():
        content = re.sub(
            r'(\s)state\s*=\s*' + re.escape(string_state),
            rf'\1state = {enum_state}',
            content
        )
    
    # Replace state in "or" chains
    for string_state, enum_state in STATE_MAP.items():
        content = re.sub(
            r'\bor\s+state\s*==\s*' + re.escape(string_state),
            f'or state == {enum_state}',
            content
        )
    
    if content != original:
        with open(filepath, 'w', encoding='utf-8') as f:
            f.write(content)
        print(f"Converted {filepath}")
        return True
    else:
        print(f"No changes needed for {filepath}")
        return False

if __name__ == '__main__':
    import sys
    filepath = r'c:\Users\patri\Documents\Falling-Leaves-Ravensguard\Main.gd'
    if len(sys.argv) > 1:
        filepath = sys.argv[1]
    
    success = convert_file(filepath)
    sys.exit(0 if success else 1)
