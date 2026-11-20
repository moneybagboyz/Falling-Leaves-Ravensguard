using System.Collections.Generic;
using Ravensguard.Content;
using Ravensguard.Sim.Building;
using Ravensguard.Sim.Characters;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Crafting;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.People;

namespace Ravensguard.Game.UI.NewGame;

/// <summary>
/// Dev scenes of the crafting screen for screenshots (P5-E): <c>smithy</c> (the station panel read from a detected smithy: two forges and a fine anvil, an arming sword with the temper choice),
/// <c>commission</c> (a smith of the place, his price and days, and a commission under way) and <c>kit</c> (mending mail in the open with a field smithing kit). Local sessions, no world.
/// </summary>
public static class CraftShotScenes
{
    static readonly EntityId Hero = CraftingSession.HeroId;

    public static (CraftingSession Session, string Recipe, bool Commission) Make(ContentDatabase db, HeroRecord hero, string scene)
    {
        switch (scene)
        {
            case "smithy":
            {
                var lib = Ravensguard.Game.World.StreetView.LoadLibrary();
                var w = lib.NewWorld();
                var e = new List<CellEdit>();
                for (int y = 0; y <= 5; y++)
                    for (int x = 0; x <= 7; x++)
                        e.Add(new CellEdit(x, y, 0, CellLayer.Structure, x is 0 or 7 || y is 0 or 5 ? "wall_stone" : "floor_flagstone"));
                e.Add(new CellEdit(3, 5, 0, CellLayer.Structure, "door_wood"));
                e.Add(new CellEdit(1, 1, 0, CellLayer.Fixture, "forge")); e.Add(new CellEdit(2, 1, 0, CellLayer.Fixture, "forge"));
                e.Add(new CellEdit(4, 3, 0, CellLayer.Fixture, "anvil")); e.Add(new CellEdit(6, 4, 0, CellLayer.Fixture, "chest"));
                w.Apply(e); w.RoofOver("roof_slate");
                var rdb = RecipeDb.Build(db, new GoodCatalog(db));
                var scan = CraftReach.Scan(w, rdb, 3, 3, 0, null, [], 255, tierAt: (x, y, z) => x == 4 && y == 3 ? ItemQuality.Fine : ItemQuality.Common);
                var s = new CraftingSession(db, hero, 9 * 3600, true, [], 255, null, scan);
                Skills(s, (Skill.Smithing, 70));
                s.Site.AddTool(s.Db, "tongs"); s.Site.AddTool(s.Db, "hammer");
                s.State.Know.SetPm(Hero, s.Db.ProfIndex("bladesmithing"), 640);
                Give(s, ("steel_bar", 3000), ("charcoal", 30_000), ("logs", 1000));
                return (s, "forge_arming_sword", false);
            }
            case "commission":
            {
                var s = new CraftingSession(db, hero, 9 * 3600, true, ["hearth"], 255, null, new ReachScan { RoomName = "" }) { Settlement = 0 };
                Skills(s, (Skill.Smithing, 20));
                s.AddLocalSmith("Hedda Smith", 72, 30_000);
                Give(s, ("steel_bar", 1400), ("logs", 300), ("charcoal", 23_000));
                var knife = s.Db["forge_knife"];
                var quotes = s.Quotes(knife, false);
                Give(s, ("steel_bar", 400), ("tool_handle", 1), ("charcoal", 4000));
                if (quotes.Count > 0) s.Order(knife, quotes[0], true);
                return (s, "forge_arming_sword", true);
            }
            default: // "kit": mail mended in the open with a field smithing kit
            {
                var s = new CraftingSession(db, hero, 15 * 3600, true, [], 255, null, new ReachScan { RoomName = "" });
                Skills(s, (Skill.Smithing, 25));
                s.Site.AddTool(s.Db, "field_smith_kit");
                s.Econ.Items.Add(new IdAllocator(), "mail_hauberk", ItemQuality.Common, Hero, [new ItemComponent("wrought_iron", 9000, 1000)], conditionPermille: 380);
                Give(s, ("scrap_iron", 300));
                return (s, "field_patch_mail", false);
            }
        }
    }

    static void Skills(CraftingSession s, params (Skill S, int Level)[] levels) { foreach (var (k, l) in levels) s.State.Book.Set(Hero, k, l * CraftMath.MicroPerLevel); }
    static void Give(CraftingSession s, params (string Good, long Qty)[] goods) { foreach (var (g, q) in goods) s.Pack.Create(s.Econ.Goods.IdOf(g), q, CreateReason.Seed); }
}
