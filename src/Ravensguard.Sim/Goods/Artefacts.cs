using Ravensguard.Content;
using Ravensguard.Sim.Core;

namespace Ravensguard.Sim.Goods;

/// <summary>A named artefact: its name, its maker (person id and name as it was) and when it was made. It fights as the masterwork it is.</summary>
public sealed record Artefact(string Name, EntityId Maker, string MakerName, long MadeSec);

/// <summary>The named artefacts of the world by item id (saved in the "crafting" section). Selling, theft and inheritance move the row, never the name.</summary>
public sealed class ArtefactBook
{
    public SortedDictionary<EntityId, Artefact> Named { get; } = [];
    public int Count => Named.Count;
    public Artefact? Of(EntityId item) => Named.GetValueOrDefault(item);

    public ulong Hash(ulong h = 14695981039346656037UL)
    {
        static ulong M(ulong h, ulong v) => (h ^ v) * 1099511628211UL;
        foreach (var (id, a) in Named)
        {
            h = M(h, id.Value); h = M(h, a.Maker.Value); h = M(h, (ulong)a.MadeSec);
            foreach (char c in a.Name) h = M(h, c);
        }
        return h;
    }
}

/// <summary>
/// Named artefacts (items-quality-and-wear 4.2, P5-A1): a Masterwork weapon, armour piece or jewel rolls <see cref="NamedPm"/> (100 pm, stream 0x531 keyed on the item id); a named item
/// gets the <see cref="ItemFlags.Named"/> flag, a name from the content's pool (<c>{epithet} of {maker}</c>, the epithet picked by the same stream) and a chronicle entry
/// <c>artefact_made</c> whose cause is the maker (the cause's id is the maker's person id, its note "maker"). Its history lines (owners, kills of named people) are appended by Phase 6. No tier above Masterwork: it is no stronger.
/// </summary>
public static class Artefacts
{
    public const int NamedPm = 100;
    public const string ChronicleType = "artefact_made";

    /// <summary>Whether a masterwork item of this id is named (one draw 0..999 of stream 0x531 under <see cref="NamedPm"/>).</summary>
    public static bool Rolls(ulong seed, EntityId item) => DetRng.Range(seed, 0, item.Value, RngStreams.NamedArtefact, 0, 0, 1000) < NamedPm;

    /// <summary>A weapon, an armour piece or a jewel (a good tagged "jewel") may carry a name.</summary>
    public static bool CanBeNamed(ContentDatabase db, string shape)
        => db.TryGet<WeaponShapeDef>(shape, out _) || db.TryGet<ArmourShapeDef>(shape, out _) || db.TryGet<GoodDef>(shape, out var g) && g.Tags.Contains("jewel");

    /// <summary>The name: the pool's pattern with an epithet (stream 0x531, draw 1, keyed on the item) and the maker's given name.</summary>
    public static string NameFor(ContentDatabase db, ulong seed, EntityId item, string makerName)
    {
        var pool = db.All<ArtefactNamesDef>().OrderBy(p => p.Id, StringComparer.Ordinal).FirstOrDefault();
        string given = makerName.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? makerName;
        if (pool == null || pool.Epithets.Count == 0) return given.Length > 0 ? $"The Blade of {given}" : "The Nameless";
        string epithet = pool.Epithets[DetRng.Range(seed, 0, item.Value, RngStreams.NamedArtefact, 1, 0, pool.Epithets.Count)];
        return pool.Pattern.Replace("{epithet}", epithet, StringComparison.Ordinal).Replace("{maker}", given.Length > 0 ? given : "Unknown", StringComparison.Ordinal);
    }

    /// <summary>
    /// Rolls a freshly made Masterwork <paramref name="item"/>: when it is named, flags the row, records it in <paramref name="book"/> and adds the chronicle entry (cause: the maker).
    /// Returns the name, or null when it is not named (or is not a masterwork that may carry one).
    /// </summary>
    public static string? TryName(ContentDatabase db, ulong seed, ItemTable items, EntityId item, EntityId maker, string makerName, long nowSec, ArtefactBook book,
        Chronicle? chronicle, IdAllocator? ids)
    {
        int row = items.IndexOf(item);
        if (row < 0) return null;
        ref var r = ref items.At(row);
        if (r.Quality != ItemQuality.Masterwork || !CanBeNamed(db, r.Shape) || !Rolls(seed, item)) return null;
        string name = NameFor(db, seed, item, makerName);
        r.Flags |= ItemFlags.Named;
        book.Named[item] = new Artefact(name, maker, makerName, nowSec);
        if (chronicle != null && ids != null)
            chronicle.Add(new ChronicleEntry(ids.Allocate(IdKind.Event), new GameTime(nowSec), ChronicleType, [item, maker], [Why.Of(CauseKind.Trade, "ch_why_artefact_maker", ChronParam.Person("maker", maker))],
                new Dictionary<string, string> { ["name"] = name, ["maker"] = makerName, ["maker_id"] = maker.Value.ToString(System.Globalization.CultureInfo.InvariantCulture), ["shape"] = r.Shape }));
        return name;
    }
}
