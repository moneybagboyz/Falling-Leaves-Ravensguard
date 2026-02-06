import json

names_data = {
    "months": [
        "Ice-Moon", "Deep-Frost", "Seed-Time", "Rain-Hand", 
        "Green-Sun", "High-Sun", "Golden-Grain", "Harvest-Moon", 
        "Leaf-Fall", "Red-Mist", "First-Snow", "Year-End"
    ],
    "first_names": [
        "Alden", "Beric", "Cedric", "Doran", "Edric", "Finn", "Garrick", "Hakon", 
        "Ivor", "Joram", "Kael", "Ludo", "Mace", "Njal", "Osric", "Piers", "Quill", 
        "Rolf", "Stig", "Tycho", "Ulf", "Vane", "Wulf", "Xander", "Yoric", "Zane"
    ],
    "last_names": [
        "the Bold", "the Cruel", "the Wise", "the Tall", "the Fair", "the Grim", 
        "the Stout", "the Swift", "the Old", "the Young", "Iron-Foot", "Wolf-Slayer", 
        "Gold-Tooth", "Half-Hand", "the Pious", "the Vile", "the Just", "the Silent"
    ]
}

with open('data/names.json', 'w') as f:
    json.dump(names_data, f, indent=2)

print("Names data extracted to data/names.json")
