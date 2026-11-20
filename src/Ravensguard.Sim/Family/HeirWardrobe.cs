using Ravensguard.Content;
using Ravensguard.Sim.Clothing;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.Inventory;
using Ravensguard.Sim.People;

namespace Ravensguard.Sim.Family;

/// <summary>
/// The heir's own clothes (family-and-lifespan 4.7, FL-14). The heir takes up the dead hero's id but not the dead hero's origin: he dresses as he was dressed as a person of the world, by his own class, occupation and body, with the
/// same stable draws the clothing book makes for any person on his id (so he stands in the very pieces the street saw him in), not by the class of an origin he never had. The pieces are real item rows he owns, worn, nothing
/// is made new by class beyond what his wardrobe already holds. An heir the wardrobe has nothing for gets the origin's outfit (<see cref="HeroOutfit"/>) as before.
/// </summary>
public static class HeirWardrobe
{
    /// <summary>What a person's wardrobe holds: shape, material and the condition of the piece in permille, in the order worn.</summary>
    public readonly record struct Piece(string Shape, string Material, int ConditionPm);

    /// <summary>The pieces a person of this row wears at <paramref name="gameSec"/>: the clothing book's own dressing of him, read and dropped (nothing in the world changes).</summary>
    public static List<Piece> For(ContentDatabase db, PersonTable pt, int row, ulong seed, long gameSec)
    {
        var res = new List<Piece>();
        if (row < 0 || row >= pt.Count) return res;
        var book = new ClothingBook(db); var id = pt.IdOf(row);
        var t = book.Track(id, pt.Sex[row], pt.Class[row], pt.Occupation[row], pt.Attribute(row, 1), seed, gameSec);
        foreach (var it in t.Worn)
        {
            ref var r = ref book.Items.Get(it);
            if (r.ComponentCount == 0) continue;
            var c = book.Items.Component(r.ComponentStart);
            res.Add(new Piece(r.Shape, c.Material, c.ConditionPermille));
        }
        return res;
    }

    /// <summary>Puts the heir's pieces on the hero (item rows owned by him, worn). Returns how many were put on; 0 when his wardrobe holds nothing and the caller dresses him by the origin.</summary>
    public static int Dress(EconomyState e, ContentDatabase db, ContainerRules rules, ItemFactory? factory, IdAllocator ids, Carried carried, IEnumerable<Piece> pieces)
    {
        int n = 0;
        foreach (var p in pieces)
        {
            if (!db.TryGet<ArmourShapeDef>(p.Shape, out _)) continue;
            var id = carried.MakeItem(e, db, rules, factory, ids, p.Shape, p.Material);
            ref var row = ref e.Items.Get(id);
            row.ConditionPermille = (short)Math.Clamp(p.ConditionPm, 0, 1000);
            if (row.ComponentCount > 0) { var c = e.Items.Component(row.ComponentStart); e.Items.SetComponent(row.ComponentStart, c with { ConditionPermille = (short)Math.Clamp(p.ConditionPm, 0, 1000) }); }
            carried.EquipItem(e, rules, id);
            n++;
        }
        return n;
    }
}
