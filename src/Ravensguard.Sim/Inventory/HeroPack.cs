using Ravensguard.Sim.Characters;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Goods;

namespace Ravensguard.Sim.Inventory;

/// <summary>
/// The hero's pack as the world holds it (P5-A4): the item rows the hero owns (with their real material and quality) and the counted goods of the pack store, both in the running world's
/// economy (saved in its section, counted by the mass audit, robbed, looted into and crafted from). What the hero fights with and crafts with is read from here, not from the record's
/// starting kit (<see cref="HeroRecord.Inventory"/>, which only seeds the pack at a new game).
/// </summary>
public static class HeroPack
{
    /// <summary>Everything carried as (shape, material, count, quality): one per item row in row order, then each counted good that names an item shape (arrows, javelins) with its count.</summary>
    public static List<HeroItem> Things(EconomyState e, Carried carried, Func<string, bool>? isShape = null)
    {
        var list = new List<HeroItem>();
        foreach (int r in carried.Rows(e))
        {
            ref var row = ref e.Items.At(r);
            string mat = row.ComponentCount > 0 ? e.Items.Component(row.ComponentStart).Material : "";
            list.Add(new HeroItem(row.Shape, mat, 1, row.Quality, (row.Flags & ItemFlags.Brittle) != 0, row.Id.Value));
        }
        if (isShape != null)
            foreach (var (g, q) in carried.Pack(e).Rows())
            {
                string id = e.Goods.StringId(g);
                if (q > 0 && isShape(id)) list.Add(new HeroItem(id, HeroRecord.DefaultMaterial(id), (int)Math.Min(int.MaxValue, q)));
            }
        return list;
    }

    /// <summary>The same list from a hero view (the game's copy of the pack at the last barrier).</summary>
    public static List<HeroItem> Things(IEnumerable<CarriedItem> items, IEnumerable<CarriedGood> goods, Func<string, bool>? isShape = null)
    {
        var list = new List<HeroItem>();
        foreach (var it in items) list.Add(new HeroItem(it.Shape, it.Material, 1, it.Quality, (it.Flags & ItemFlags.Brittle) != 0, it.Id.Value));
        if (isShape != null) foreach (var g in goods) if (g.Qty > 0 && isShape(g.Id)) list.Add(new HeroItem(g.Id, HeroRecord.DefaultMaterial(g.Id), (int)Math.Min(int.MaxValue, g.Qty)));
        return list;
    }

    /// <summary>The craft tools the pack holds: each item row or good whose shape is a <c>craft_tool</c> id (a dagger cuts as a knife), in pack order, no repeats.</summary>
    public static List<string> Tools(IEnumerable<string> shapes, Func<string, bool> isTool)
    {
        var list = new List<string>();
        foreach (var s in shapes)
        {
            string id = s is "dagger" ? "knife" : s;
            if (isTool(id) && !list.Contains(id)) list.Add(id);
        }
        return list;
    }
}
