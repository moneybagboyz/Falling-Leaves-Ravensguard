import json

ai_config = {
    "governor_personalities": ["builder", "greedy", "balanced", "cautious"],
    "lord_doctrines": ["conqueror", "defender", "raider", "merchant_prince"],
    "material_tiers": {
        "1": "leather",
        "2": "copper",
        "3": "bronze",
        "4": "iron",
        "5": "steel"
    }
}

with open('data/ai_config.json', 'w') as f:
    json.dump(ai_config, f, indent=2)

print("AI configuration extracted to data/ai_config.json")
