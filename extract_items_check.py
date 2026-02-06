import re
import json

# Read the GameData.gd file
with open('src/core/GameData.gd', 'r', encoding='utf-8') as f:
    content = f.read()

# Find the ITEMS constant using regex
# Match from "const ITEMS = {" to the closing "}" before the next "const"
pattern = r'const ITEMS = \{(.*?)\n\n# --- SETTLEMENTS'
match = re.search(pattern, content, re.DOTALL)

if match:
    items_content = match.group(1).strip()
    
    # We need to manually parse this GDScript dictionary into Python
    # For now, let's output a marker file and handle conversion manually
    print("Found ITEMS constant")
    print(f"Size: {len(items_content)} characters")
    print("Lines:", items_content.count('\n'))
    
    # Save the raw content for manual inspection
    with open('items_raw.txt', 'w') as f:
        f.write(items_content)
    
    print("\nSaved raw ITEMS content to items_raw.txt")
    print("Note: GDScript->JSON conversion requires manual work for complex structures")
    print("Consider using a smaller subset or creating ItemsData.gd that loads from GDScript directly")
else:
    print("Could not find ITEMS constant")
