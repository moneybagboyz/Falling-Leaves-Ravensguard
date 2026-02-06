import json

materials = {
	"flesh": {"hardness": 0, "density": 1, "impact_yield": 10, "shear_yield": 5, "elasticity": 0.1},
	"bone": {"hardness": 30, "density": 2, "impact_yield": 100, "shear_yield": 50, "elasticity": 0.05},
	"cloth": {"hardness": 2, "density": 1, "impact_yield": 5, "shear_yield": 20, "elasticity": 0.5},
	"linen": {"hardness": 2, "density": 1, "impact_yield": 4, "shear_yield": 25, "elasticity": 0.4},
	"wool": {"hardness": 1, "density": 1.5, "impact_yield": 10, "shear_yield": 15, "elasticity": 0.6},
	"silk": {"hardness": 1, "density": 0.5, "impact_yield": 2, "shear_yield": 80, "elasticity": 0.8},
	"leather": {"hardness": 10, "density": 2, "impact_yield": 40, "shear_yield": 30, "elasticity": 0.3},
	"wood": {"hardness": 15, "density": 3, "impact_yield": 80, "shear_yield": 40, "elasticity": 0.2},
	"tin": {"hardness": 10, "density": 7, "impact_yield": 80, "shear_yield": 40, "elasticity": 0.01},
	"copper": {"hardness": 25, "density": 9, "impact_yield": 200, "shear_yield": 120, "elasticity": 0.01},
	"bronze": {"hardness": 35, "density": 11, "impact_yield": 250, "shear_yield": 180, "elasticity": 0.01},
	"iron": {"hardness": 40, "density": 10, "impact_yield": 300, "shear_yield": 200, "elasticity": 0.01},
	"steel": {"hardness": 60, "density": 12, "impact_yield": 500, "shear_yield": 400, "elasticity": 0.02},
	"silver": {"hardness": 20, "density": 15, "impact_yield": 150, "shear_yield": 100, "elasticity": 0.01},
	"gold": {"hardness": 15, "density": 25, "impact_yield": 100, "shear_yield": 50, "elasticity": 0.01},
	"lead": {"hardness": 5, "density": 11, "impact_yield": 150, "shear_yield": 20, "elasticity": 0.01},
	"stone": {"hardness": 50, "density": 5, "impact_yield": 400, "shear_yield": 100, "elasticity": 0.01}
}

with open('data/materials.json', 'w') as f:
    json.dump(materials, f, indent=2)

print("MATERIALS extracted to data/materials.json")
