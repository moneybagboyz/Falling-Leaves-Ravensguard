namespace Ravensguard.Sim.Goods;

/// <summary>Where an item spends the day, for rust and rot: dry (indoors, under a roof), damp (a store RoomDetector v2 says is not indoor), or wet (carried with the <c>Wet</c> flag).</summary>
public enum Exposure : byte { Dry, Damp, Wet }

/// <summary>
/// Rust and rot (items-quality-and-wear 4.4, P5-A1), once a game day per store (the H4 slice), only over the stores flagged damp and over wet carried things, never per item per tick.
/// Iron and steel lose <see cref="RustPmPerDay"/> a day (mail twice that: rings), unless oiled (<see cref="ItemFlags.Oiled"/>, <see cref="OilDays"/> days from <c>oil_item</c>); leather and
/// wood that stay wet lose <see cref="RotPmPerDay"/>. At 0 the item falls apart into scrap (<see cref="ToolWear.Break"/>). The caller says which rows lie in damp or wet places: the damp
/// flag of a store comes from RoomDetector v2's <c>Indoor</c> (P5-A3; the H4 slice is wired when construction places stores in rooms, P5-B).
/// </summary>
public static class Corrosion
{
    public const int RustPmPerDay = 2, MailRustMult = 2, RotPmPerDay = 1, OilDays = 30;

    /// <summary>The material rusts (iron and steel; bronze and silver do not, in this model).</summary>
    public static bool Rusts(string material) => material is "iron" or "steel" or "wrought_iron" or "pig_iron";
    /// <summary>The material rots when it stays wet.</summary>
    public static bool Rots(string? category) => category is "leather" or "wood";

    /// <summary>
    /// One day for one row. <paramref name="isMail"/>: the row is a ring garment (rusts at x2). <paramref name="oiledUntilDay"/>: the last day its oil protects (the flag is cleared after it).
    /// Returns the condition lost; when the row reached 0 it has been broken into scrap in <paramref name="ownerStore"/>.
    /// </summary>
    public static int Day(ItemTable items, int row, Exposure where, int day, bool isMail, int oiledUntilDay, Store? ownerStore, Func<string, string?> materialCategory)
    {
        ref var r = ref items.At(row);
        if ((r.Flags & ItemFlags.Oiled) != 0 && day > oiledUntilDay) r.Flags &= ~ItemFlags.Oiled;
        if (where == Exposure.Dry || r.ComponentCount == 0) return 0;
        string main = items.Component(r.ComponentStart).Material;
        int lose = 0;
        if (Rusts(main) && (r.Flags & ItemFlags.Oiled) == 0) lose = RustPmPerDay * (isMail ? MailRustMult : 1);
        else if (where == Exposure.Wet && Rots(materialCategory(main))) lose = RotPmPerDay;
        if (lose == 0) return 0;
        int before = r.ConditionPermille;
        r.ConditionPermille = (short)Math.Max(0, before - lose);
        if (r.ConditionPermille == 0) ToolWear.Break(items, row, ownerStore, materialCategory);
        return before - Math.Max(0, before - lose);
    }
}
