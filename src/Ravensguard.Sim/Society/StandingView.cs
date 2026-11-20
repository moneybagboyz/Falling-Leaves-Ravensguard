using Ravensguard.Sim.Core;
using Ravensguard.Sim.Save;

namespace Ravensguard.Sim.Society;

/// <summary>One deed behind a standing (the Standing tab's "why", reputation 6).</summary>
public sealed record WhyLine(EntityId Entry, string DeedId, int Day, int ValuePm);

/// <summary>One group the person has a standing with: its name, the value and its band, how many have heard, and the three deeds that matter most.</summary>
public sealed record StandingLine(GroupRef Group, string Name, int ValuePm, StandingBand Band, int Heard, int Members, IReadOnlyList<WhyLine> Why);

/// <summary>The hero's standing as the character screen shows it (reputation 6), built at a barrier: every group with a value, renown, and the marks as loc-key suffixes (<c>thief</c>, <c>outlaw</c> and so on).</summary>
public sealed record StandingView(IReadOnlyList<StandingLine> Lines, int RenownPm, IReadOnlyList<(string Mark, string Where)> Marks, int Day)
{
    public static readonly StandingView Empty = new([], 0, [], 0);

    /// <summary>Builds the view of <paramref name="person"/> (the hero's id) from the world; the Faction screen reads the realm and fief lines of the same view.</summary>
    public static StandingView Of(WorldState st, DeedTable deeds, EntityId person, int day)
    {
        if (st.Standing is not { } book) return Empty with { Day = day };
        var region = st.Population.Region; var religions = Ravensguard.Sim.Faith.FaithWiring.ReligionsOf(st); var map = new RegionGroups(region, st.Population.People, day, religions);
        string Name(GroupRef g) => g.Kind switch
        {
            GroupKind.Settlement when g.Index >= 0 && g.Index < region.Settlements.Count => region.Settlements[g.Index].Name,
            GroupKind.Fief when g.Index >= 0 && g.Index < region.Fiefs.Count => region.Fiefs[g.Index].Name,
            GroupKind.Realm => region.Realms.FirstOrDefault(r => r.HistoryIndex == g.Index)?.Name ?? "",
            GroupKind.Religion => religions.Get(g.Index)?.Name ?? "",
            _ => "",
        };
        var lines = new List<StandingLine>();
        foreach (var (g, row) in book.RowsOf(person))
        {
            int v = Standing.Of(book, person, g);
            lines.Add(new StandingLine(g, Name(g), v, Standing.BandOf(v), row.Heard, map.Members(g),
                [.. book.Why(person, g, deeds, map, day).Select(c => new WhyLine(c.Entry, c.DeedId, c.Day, c.ValuePm))]));
        }
        // an outlaw's realm shows even with no row of its own
        if (book.Marks.Of(person) is { } m)
            foreach (int r in m.OutlawRealms) if (!lines.Any(l => l.Group == GroupRef.Realm(r))) lines.Add(new StandingLine(GroupRef.Realm(r), Name(GroupRef.Realm(r)), -1000, StandingBand.Hated, 0, map.Members(GroupRef.Realm(r)), []));
        lines.Sort((a, b) => a.Group.Kind != b.Group.Kind ? ((byte)b.Group.Kind).CompareTo((byte)a.Group.Kind) : a.Group.Index.CompareTo(b.Group.Index));     // realms, then fiefs, then settlements
        var marks = new List<(string, string)>();
        if (book.Marks.Of(person) is { } ms)
        {
            if ((ms.Brands & Brand.Thief) != 0) marks.Add(("thief", ""));
            if ((ms.Brands & Brand.Murderer) != 0) marks.Add(("murderer", ""));
            if ((ms.Brands & Brand.Runaway) != 0) marks.Add(("runaway", ""));
            foreach (int r in ms.OutlawRealms) marks.Add(("outlaw", Name(GroupRef.Realm(r))));
            foreach (int r in ms.Excommunicated) marks.Add(("excommunicated", ""));
            if (ms.Enslaved) marks.Add(("enslaved", ""));
            if (ms.Pardoned) marks.Add(("pardoned", ""));
        }
        return new StandingView(lines, book.RenownOf(person), marks, day);
    }
}
