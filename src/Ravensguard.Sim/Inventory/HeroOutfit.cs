using Ravensguard.Content;
using Ravensguard.Sim.Characters;
using Ravensguard.Sim.Clothing;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.People;

namespace Ravensguard.Sim.Inventory;

/// <summary>
/// The clothes a new hero starts in (clothing spec 4.1 starting wardrobes, trade-inventory-character spec 3.2): the wardrobe of the origin's class (a minor heir dresses like gentry, a peddler or a
/// discharged soldier like a burgher, everyone else like a peasant), every piece of it the class owns for sure or most often, one pair of footwear, the cloak always (a traveller keeps it in summer
/// too), the hood outside summer and gloves in the cold seasons. Pieces for an occupation (a guard's gambeson) are left to the kit. The pieces are real item rows, worn, part of the inventory.
/// </summary>
public static class HeroOutfit
{
    public static SocialClass ClassOf(string origin)
        => origin is "minor_heir" or "disgraced_squire" ? SocialClass.Gentry : origin is "peddler" or "discharged_soldier" ? SocialClass.Burgher : SocialClass.Peasant;

    static readonly HashSet<string> Footwear = new(StringComparer.Ordinal) { "shoes", "boots" };
    static readonly HashSet<string> HandWear = new(StringComparer.Ordinal) { "gloves", "mittens" };

    /// <summary>The (shape, material) pieces for this hero at <paramref name="gameSec"/>, in wardrobe order; empty when the content has no wardrobe.</summary>
    public static List<(string Shape, string Material)> For(ContentDatabase db, HeroRecord hero, long gameSec)
    {
        var cls = ClassOf(hero.Origin).ToString();
        var w = db.All<WardrobeDef>().OrderBy(x => x.Id, StringComparer.Ordinal).FirstOrDefault(x => x.Class == cls)
             ?? db.All<WardrobeDef>().OrderBy(x => x.Id, StringComparer.Ordinal).FirstOrDefault();
        var list = new List<(string, string)>();
        if (w == null) return list;
        bool cold = ClothingBook.ColdSeason(gameSec);
        bool summer = new GameTime(gameSec).Season == Season.Summer;
        var feet = w.Pieces.Where(p => Footwear.Contains(p.Shape) && p.Occupations.Count == 0).OrderByDescending(p => p.ChancePm).ThenBy(p => p.Shape, StringComparer.Ordinal).FirstOrDefault();
        foreach (var p in w.Pieces)
        {
            if (p.Occupations.Count > 0 || !db.TryGet<ArmourShapeDef>(p.Shape, out _)) continue;
            if (Footwear.Contains(p.Shape)) { if (p == feet) list.Add((p.Shape, p.Material)); continue; }
            if (p.Season == "warm" && cold) continue;
            if (p.Season == "cold")
            {
                bool take = p.Shape == "cloak" || (p.Shape == "hood" && !summer) || (HandWear.Contains(p.Shape) && cold) || (cold && p.ChancePm >= 500);
                if (!take) continue;
            }
            else if (p.ChancePm < 500) continue;
            list.Add((p.Shape, p.Material));
        }
        return list;
    }
}
