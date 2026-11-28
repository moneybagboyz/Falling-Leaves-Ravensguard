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

    /// <summary>The default pool (the row with no culture): serves every culture without a table of its own.</summary>
    private static ArtefactNamesDef? DefaultPool(ContentDatabase db) => db.All<ArtefactNamesDef>().Where(p => p.Culture.Length == 0).OrderBy(p => p.Id, StringComparer.Ordinal).FirstOrDefault();

    /// <summary>The pool of a continent culture (by its name, e.g. "Aldmark"), or null when it has none (P8-LM-2).</summary>
    public static ArtefactNamesDef? PoolOf(ContentDatabase db, string culture)
        => culture.Length == 0 ? null : db.All<ArtefactNamesDef>().FirstOrDefault(p => string.Equals(p.Culture, culture, StringComparison.Ordinal));

    /// <summary>
    /// The name: the pool's pattern with an epithet (stream 0x531, draw 1, keyed on the item) and the maker's given name. With a <paramref name="culture"/> that has its own table of weapons (P8-LM-2) the epithet
    /// is one of its weapons; any other culture (or none) draws from the default pool, as before.
    /// </summary>
    public static string NameFor(ContentDatabase db, ulong seed, EntityId item, string makerName, string culture = "")
    {
        var own = PoolOf(db, culture); var pool = own is { Weapons.Count: > 0 } ? own : DefaultPool(db);
        var epithets = own is { Weapons.Count: > 0 } ? own.Weapons : pool?.Epithets;
        string given = makerName.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? makerName;
        if (pool == null || epithets == null || epithets.Count == 0) return given.Length > 0 ? $"The Blade of {given}" : "The Nameless";
        string epithet = epithets[DetRng.Range(seed, 0, item.Value, RngStreams.NamedArtefact, 1, 0, epithets.Count)];
        return pool.Pattern.Replace("{epithet}", epithet, StringComparison.Ordinal).Replace("{maker}", given.Length > 0 ? given : "Unknown", StringComparison.Ordinal);
    }

    /// <summary>The weapon epithet table the culture names its weapons from (its own, or the default pool's).</summary>
    public static IReadOnlyList<string> WeaponTable(ContentDatabase db, string culture) => PoolOf(db, culture) is { Weapons.Count: > 0 } p ? p.Weapons : DefaultPool(db)?.Epithets ?? [];

    /// <summary>A relic's name from the culture's own table (stream 0x531, draw 2, keyed on <paramref name="key"/>: an item or a person's id); the default pool has no relics, so a culture without a table gets "The Old Relic".</summary>
    public static string RelicName(ContentDatabase db, ulong seed, ulong key, string culture)
        => PoolOf(db, culture) is { Relics.Count: > 0 } p ? p.Relics[DetRng.Range(seed, 0, key, RngStreams.NamedArtefact, 2, 0, p.Relics.Count)] : "The Old Relic";

    /// <summary>A family heirloom's name (a worn blade, a cup, a ring) from the culture's own table (stream 0x531, draw 3).</summary>
    public static string HeirloomName(ContentDatabase db, ulong seed, ulong key, string culture)
        => PoolOf(db, culture) is { Heirlooms.Count: > 0 } p ? p.Heirlooms[DetRng.Range(seed, 0, key, RngStreams.NamedArtefact, 3, 0, p.Heirlooms.Count)] : "The Old Heirloom";

    /// <summary>
    /// Rolls a freshly made Masterwork <paramref name="item"/>: when it is named, flags the row, records it in <paramref name="book"/> and adds the chronicle entry (cause: the maker).
    /// Returns the name, or null when it is not named (or is not a masterwork that may carry one).
    /// </summary>
    public static string? TryName(ContentDatabase db, ulong seed, ItemTable items, EntityId item, EntityId maker, string makerName, long nowSec, ArtefactBook book,
        Chronicle? chronicle, IdAllocator? ids, string culture = "")
    {
        int row = items.IndexOf(item);
        if (row < 0) return null;
        ref var r = ref items.At(row);
        if (r.Quality != ItemQuality.Masterwork || !CanBeNamed(db, r.Shape) || !Rolls(seed, item)) return null;
        string name = NameFor(db, seed, item, makerName, culture);
        r.Flags |= ItemFlags.Named;
        book.Named[item] = new Artefact(name, maker, makerName, nowSec);
        if (chronicle != null && ids != null)
        {
            var madeBy = Why.Of(CauseKind.Trade, "ch_why_artefact_maker", ChronParam.Person("maker", maker));
            chronicle.Add(new ChronicleEntry(ids.Allocate(IdKind.Event), new GameTime(nowSec), ChronicleType, [item, maker], [madeBy],
                new Dictionary<string, string> { ["name"] = name, ["maker"] = makerName, ["maker_id"] = maker.Value.ToString(System.Globalization.CultureInfo.InvariantCulture), ["shape"] = r.Shape }));
        }
        return name;
    }
}
