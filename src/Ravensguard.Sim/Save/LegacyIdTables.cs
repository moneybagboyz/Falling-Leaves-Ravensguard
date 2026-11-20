namespace Ravensguard.Sim.Save;

/// <summary>
/// The id tables of saves made before the "tables" section existed, frozen from the content they were made with, so their indices are still read by name.
/// <see cref="V8"/>: a v8 save without the section (Phase 5 steps 1 and 2, the content of 69fa205: 104 goods, 105 craft recipes, 48 proficiencies). <see cref="V7"/>: what the
/// v7 -> v8 step stamps on a Phase 4 save (the content of 1a10645: the same goods, business kinds, book recipes and extraction kinds; Phase 3's 34 craft recipes and 12 proficiencies).
/// Never edit these lists: they describe saves that exist.
/// </summary>
public static class LegacyIdTables
{
    public static IdTables V8 => Make(CraftV8, ProfV8);
    public static IdTables V7 => Make(CraftV7, ProfV7);

    private static IdTables Make(string[] craft, string[] prof)
    {
        var t = new IdTables();
        t.Tables[IdTables.Goods] = Goods; t.Tables[IdTables.CraftRecipes] = craft; t.Tables[IdTables.Proficiencies] = prof;
        t.Tables[IdTables.Businesses] = Kinds; t.Tables[IdTables.BookRecipes] = Book; t.Tables[IdTables.Extractions] = Ext;
        return t;
    }

    private static readonly string[] Goods =
    [
        "ale", "arming_sword", "ash", "bandage", "barley", "barrel", "beans", "bedroll", "bone", "bread", "bricks", "bronze", "candles", "cart", "charcoal", "cheese", "clay", "cloth_dyed",
        "cloth_fulled", "cloth_linen", "cloth_wool", "clothes", "coal", "cookware", "copper", "copper_ore", "cow", "dough_sour", "dye", "eggs", "firewood", "fish_fresh", "fish_salted",
        "flatbread", "flour", "fullers_earth", "galena", "glass", "hay", "herbs", "hide_raw", "hood", "horseshoe_set", "iron_bar", "iron_bloom", "iron_ore", "lead", "lead_argentiferous",
        "leather", "limestone", "logs", "lye_soap", "malt", "meat_fresh", "meat_salted", "milk", "nails", "oak_bark", "oats", "offal", "peas", "peat", "pig", "potash", "pottage",
        "pottery", "poultice", "quicklime", "rags", "roast_meat", "rope", "rye", "saddle", "salt", "sand", "sawdust", "scrap_iron", "shavings", "sheep", "shoes", "silver", "snare",
        "splint", "steel_bar", "sticks", "stone_dressed", "stone_raw", "straw", "tallow", "thatch", "timber_planks", "tin", "tin_ore", "tool_handle", "tools", "torch", "twine",
        "vegetables", "waste", "water", "wheat", "whey", "wool_raw", "yarn"
    ];

    private static readonly string[] CraftV8 =
    [
        "bake_bread", "bake_flatbread", "bake_loaf", "bandage_linen", "beat_out_plate", "bind_thatch", "braid_rope", "brain_tan_hide", "brew_ale", "brew_home_ale", "burn_charcoal",
        "burn_lime", "carve_handle", "cast_bronze_goods", "cobble_shoes", "cook_pottage", "cupel_silver", "cut_frame_plank", "dress_stone", "dye_cloth", "field_patch_mail", "fire_bricks",
        "forge_arming_sword", "forge_bar", "forge_breastplate", "forge_chisel", "forge_hand_axe", "forge_helmet", "forge_hinges", "forge_horseshoe_set", "forge_iron_tyre", "forge_knife",
        "forge_longsword", "forge_mail_shirt", "forge_nails", "forge_nails_small", "forge_spade", "forge_spear", "forge_sword", "forge_tools", "full_cloth", "grind_meal",
        "herbal_poultice", "kindle_fire", "lye_soap", "make_axle", "make_barrel", "make_bronze", "make_candle", "make_candles", "make_cargo_bed", "make_cart", "make_cheese", "make_glass",
        "make_pottery", "make_saddle", "make_shafts", "make_snare", "make_soft_cheese", "make_solid_wheel", "make_spoked_wheel", "make_steel", "make_torch", "malt_barley", "mend_garment",
        "mend_tool", "mill_flour", "oil_item", "patch_garment", "patch_leather", "practice_billet", "practice_sampler", "practice_spoon", "reedge_blade", "refit_garment", "refit_mail",
        "refit_plate", "reforge_item", "repair_cart_wheel", "rerivet_mail", "roast_meat", "salt_fish", "salt_meat", "salt_meat_tub", "salvage_armour", "salvage_mail", "saw_planks",
        "sew_bedroll", "sew_gambeson", "sew_hood", "sew_shirt", "sharpen_blade", "slaughter_cow", "slaughter_pig", "slaughter_sheep", "smelt_bloom", "smelt_copper", "smelt_galena",
        "smelt_tin", "spin_twine", "spin_yarn", "tailor_clothes", "tan_hides", "weave_cloth", "wooden_splint"
    ];

    private static readonly string[] ProfV8 =
    [
        "armoursmithing", "blacksmithing", "blacksmithing_basics", "bladesmithing", "bloomery_smelting", "bread_making", "brewing_basics", "brewing_full", "bricklaying", "bronze_casting",
        "butchery_full", "cartwright", "cheesemaking", "cobbling", "cookery", "coopering", "daubing", "fire_craft", "first_aid", "fletching", "fulling_dyeing", "glassmaking", "glazing",
        "herbalism", "joinery", "lime_burning", "locksmithing", "malting", "masonry_basics", "metalworking_principles", "milling", "needlework", "plate_armour", "pottery", "preserving",
        "riveted_mail", "saddlery", "shipwright", "silver_refining", "stone_dressing", "tailoring", "tanning_bark", "thatching", "turning", "tyre_shrinking", "weaving", "wheelwright",
        "woodcraft"
    ];

    private static readonly string[] CraftV7 =
    [
        "bake_flatbread", "bake_loaf", "bandage_linen", "braid_rope", "brain_tan_hide", "brew_home_ale", "carve_handle", "cook_pottage", "forge_horseshoe_set", "forge_nails_small",
        "grind_meal", "herbal_poultice", "kindle_fire", "lye_soap", "make_candle", "make_snare", "make_soft_cheese", "make_torch", "mend_garment", "mend_tool", "patch_garment",
        "practice_billet", "practice_sampler", "practice_spoon", "refit_garment", "repair_cart_wheel", "roast_meat", "salt_meat", "sew_bedroll", "sew_hood", "sew_shirt", "sharpen_blade",
        "spin_twine", "wooden_splint"
    ];

    private static readonly string[] ProfV7 =
    [
        "blacksmithing_basics", "bread_making", "brewing_basics", "cookery", "fire_craft", "first_aid", "herbalism", "metalworking_principles", "needlework", "preserving", "tailoring",
        "woodcraft"
    ];

    private static readonly string[] Kinds =
    [
        "bakery", "brewery", "butchery", "carpentry", "charcoal_crew", "cobbler", "fishery", "manor", "masonry", "merchant_stall", "mill", "mine", "smithy", "tannery", "tavern", "weavery",
        "woodlot"
    ];

    private static readonly string[] Book =
    [
        "bake_bread", "bind_thatch", "brew_ale", "burn_charcoal", "burn_lime", "cast_bronze_goods", "cobble_shoes", "cupel_silver", "dress_stone", "dye_cloth", "fire_bricks", "forge_bar",
        "forge_nails", "forge_sword", "forge_tools", "full_cloth", "make_barrel", "make_bronze", "make_candles", "make_cart", "make_cheese", "make_glass", "make_pottery", "make_saddle",
        "make_steel", "malt_barley", "mill_flour", "salt_fish", "salt_meat", "saw_planks", "slaughter_cow", "slaughter_pig", "slaughter_sheep", "smelt_bloom", "smelt_copper",
        "smelt_galena", "smelt_tin", "spin_yarn", "tailor_clothes", "tan_hides", "weave_cloth"
    ];

    private static readonly string[] Ext =
    [
        "clay_digging", "firewood_cutting", "fishing_shore", "foraging", "hunting", "mining_coal", "mining_copper", "mining_iron", "mining_salt", "mining_silver", "mining_tin",
        "peat_cutting", "quarrying", "sand_digging", "sea_salt", "woodcutting"
    ];
}
