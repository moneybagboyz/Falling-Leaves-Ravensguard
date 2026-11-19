using Ravensguard.Sim.Clothing;

namespace Ravensguard.Sim.Combat.Resolver2;

/// <summary>
/// The worn layers of a person as the resolver's armour layers (resolver-v2 2): what <see cref="ClothingBook"/> says the person wears, with each
/// item's shape, material, quality, condition and fit, so clothing and armour are one list (plan §2.10). Wear from a blow goes back to the items.
/// </summary>
public static class ClothingArmour
{
    /// <summary>Fills <paramref name="into"/> with the person's worn pieces, inner to outer (the resolver sorts outside in); returns the count.</summary>
    public static int Fill(ClothingBook book, Tracked t, CombatTablesV2 tables, WornV2[] into)
    {
        int n = 0;
        foreach (var id in t.Worn)
        {
            if (n >= into.Length) break;
            // one entry per worn item, in the same order (ApplyWear maps the trace's piece index back to t.Worn); a garment row always has its material
            ref var row = ref book.Items.Get(id);
            string material = row.ComponentCount > 0 ? book.Items.Component(row.ComponentStart).Material
                : book.Garments[row.Shape].DefaultMaterial ?? throw new InvalidOperationException($"worn item {id} has no material");
            var ex = book.ExtraOf(id);
            into[n++] = WornV2.Of(tables.Armour(row.Shape, material), CombatTablesV2.QualityPmOf(row.Quality), row.ConditionPermille, ex.FitCm3 > 0 ? ex.FitCm3 : 70000);
        }
        return n;
    }

    /// <summary>Applies the blow's wear to the worn items (a cut tears cloth 30 pm, mail 10, plate 5; a crush less).</summary>
    public static void ApplyWear(ClothingBook book, Tracked t, BlowResult r)
    {
        foreach (var s in r.StepSpan)
        {
            if (!s.IsArmour || s.WearPm <= 0 || s.Piece < 0 || s.Piece >= t.Worn.Count) continue;
            ref var row = ref book.Items.Get(t.Worn[s.Piece]);
            row.ConditionPermille = (short)Math.Max(0, row.ConditionPermille - s.WearPm);
        }
    }
}
