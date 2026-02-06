import json

flora_table = {
	"plains": [
		{"name": "Wild Berries", "symbol": "%", "loot": {"grain": 1}, "chance": 0.01},
		{"name": "Healing Herbs", "symbol": "?", "loot": {"spices": 1}, "chance": 0.005},
		{"name": "Hemp", "symbol": ";", "loot": {"linen": 2}, "chance": 0.01},
		{"name": "Tall Grass", "symbol": "\"", "loot": {}, "chance": 0.05},
		{"name": "Wildflowers", "symbol": ":", "loot": {}, "chance": 0.02},
		{"name": "Dandelion", "symbol": ".", "loot": {}, "chance": 0.01}
	],
	"forest": [
		{"name": "Mushrooms", "symbol": ",", "loot": {"grain": 1}, "chance": 0.02},
		{"name": "Wild Orchard", "symbol": "f", "loot": {"grain": 3}, "chance": 0.01},
		{"name": "Hardwood Sapling", "symbol": "t", "loot": {"wood": 1}, "chance": 0.03},
		{"name": "Ferns", "symbol": "v", "loot": {}, "chance": 0.04},
		{"name": "Mossy Rock", "symbol": "o", "loot": {}, "chance": 0.02},
		{"name": "Ancient Roots", "symbol": "w", "loot": {}, "chance": 0.01}
	],
	"jungle": [
		{"name": "Sugar Cane", "symbol": "!", "loot": {"spices": 2}, "chance": 0.02},
		{"name": "Cotton", "symbol": ";", "loot": {"cloth": 2}, "chance": 0.01},
		{"name": "Exotic Fruit", "symbol": "f", "loot": {"grain": 2, "spices": 1}, "chance": 0.01},
		{"name": "Orchid", "symbol": "x", "loot": {}, "chance": 0.03},
		{"name": "Vines", "symbol": "s", "loot": {}, "chance": 0.05},
		{"name": "Dense Rubber Tree", "symbol": "T", "loot": {"peat": 2}, "chance": 0.01}
	],
	"desert": [
		{"name": "Aloe", "symbol": ";", "loot": {"spices": 1}, "chance": 0.04},
		{"name": "Prickly Pear", "symbol": "p", "loot": {"grain": 1}, "chance": 0.03},
		{"name": "Sagebrush", "symbol": ",", "loot": {"wood": 1}, "chance": 0.10},
		{"name": "Cactus", "symbol": "Y", "loot": {}, "chance": 0.08},
		{"name": "Dead Scrub", "symbol": "\"", "loot": {}, "chance": 0.15},
		{"name": "Tumbleweed", "symbol": "o", "loot": {}, "chance": 0.02}
	],
	"tundra": [
		{"name": "Lichens", "symbol": ",", "loot": {"peat": 1}, "chance": 0.12},
		{"name": "Snowberries", "symbol": "%", "loot": {"grain": 1}, "chance": 0.04},
		{"name": "Hardy Grass", "symbol": "\"", "loot": {}, "chance": 0.08},
		{"name": "Frozen Twigs", "symbol": "x", "loot": {}, "chance": 0.10},
		{"name": "Ice Shards", "symbol": "^", "loot": {}, "chance": 0.05}
	],
	"mountain": [
		{"name": "Alpine Pine", "symbol": "t", "loot": {"wood": 2}, "chance": 0.08},
		{"name": "Cliff Flower", "symbol": "v", "loot": {"spices": 1}, "chance": 0.03},
		{"name": "Slate Patch", "symbol": "=", "loot": {}, "chance": 0.15},
		{"name": "Juniper", "symbol": "j", "loot": {}, "chance": 0.06},
		{"name": "Thistle", "symbol": "x", "loot": {}, "chance": 0.10}
	],
	"hills": [
		{"name": "Heather", "symbol": "v", "loot": {}, "chance": 0.15},
		{"name": "Wild Rye", "symbol": "i", "loot": {"grain": 1}, "chance": 0.10},
		{"name": "Gorse", "symbol": "*", "loot": {}, "chance": 0.08},
		{"name": "Bramble", "symbol": "&", "loot": {}, "chance": 0.12}
	],
	"arctic": [
		{"name": "Frost Moss", "symbol": ",", "loot": {}, "chance": 0.02},
		{"name": "Exposed Shale", "symbol": "=", "loot": {}, "chance": 0.05},
		{"name": "Blue Ice Mound", "symbol": "^", "loot": {}, "chance": 0.03}
	],
	"water": [
		{"name": "Kelp Forest", "symbol": "s", "loot": {}, "chance": 0.15},
		{"name": "Reeds", "symbol": "i", "loot": {"linen": 1}, "chance": 0.20},
		{"name": "Water Lily", "symbol": "o", "loot": {}, "chance": 0.05},
		{"name": "Coral Reef", "symbol": "*", "loot": {}, "chance": 0.03}
	]
}

with open('data/flora_table.json', 'w') as f:
    json.dump(flora_table, f, indent=2)

print("FLORA_TABLE extracted to data/flora_table.json")
