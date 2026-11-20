using Ravensguard.Content;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Law;

namespace Ravensguard.Sim.Heists;

/// <summary>
/// The numbers and the templates of the heist as the sim reads them (heists-and-jailbreaks 3): from the content database, or the spec's defaults when a world runs without content. Templates are kept in id order; the lock
/// table is indexed by quality.
/// </summary>
public sealed class HeistRules
{
    public static readonly HeistRules Default = new(new HeistSettingsDef { Id = "heists" }, DefaultLocks(), [], LawContent.Default.S);

    public HeistSettingsDef S { get; }
    public LawSettingsDef Law { get; }
    private readonly HeistLockDef[] _locks;
    private readonly List<HeistTemplateDef> _templates;
    private readonly Dictionary<string, HeistMap> _maps = new(StringComparer.Ordinal);

    public HeistRules(HeistSettingsDef s, IEnumerable<HeistLockDef> locks, IEnumerable<HeistTemplateDef> templates, LawSettingsDef law)
    {
        S = s; Law = law;
        var l = locks.OrderBy(x => x.Quality).ToList();
        _locks = l.Count == 0 ? DefaultLocks() : [.. l];
        _templates = [.. templates.OrderBy(t => t.Id, StringComparer.Ordinal)];
    }

    public static HeistRules From(ContentDatabase? db) =>
        db == null ? Default : new HeistRules(db.All<HeistSettingsDef>().OrderBy(s => s.Id, StringComparer.Ordinal).FirstOrDefault() ?? new HeistSettingsDef { Id = "heists" }, db.All<HeistLockDef>(), db.All<HeistTemplateDef>(), LawContent.From(db).S);

    private static HeistLockDef[] DefaultLocks() =>
    [
        new() { Id = "crude", Quality = 0, PickS = 20, Offset = 0, BreakS = 8 }, new() { Id = "common", Quality = 1, PickS = 40, Offset = -10, BreakS = 14 }, new() { Id = "good", Quality = 2, PickS = 60, Offset = -20, BreakS = 20 },
        new() { Id = "fine", Quality = 3, PickS = 120, Offset = -35, BreakS = 30 }, new() { Id = "masterwork", Quality = 4, PickS = 240, Offset = -50, BreakS = 45 },
    ];

    public HeistLockDef Lock(int quality) => _locks[Math.Clamp(quality, 0, _locks.Length - 1)];
    public IReadOnlyList<HeistTemplateDef> Templates => _templates;
    public HeistTemplateDef? Template(string id) => _templates.FirstOrDefault(t => t.Id == id);

    /// <summary>The parsed map of a template (cached; the map is never changed, play's changes live in the site state).</summary>
    public HeistMap? Map(string id)
    {
        lock (_maps)
        {
            if (_maps.TryGetValue(id, out var m)) return m;
            var t = Template(id); if (t == null) return null;
            return _maps[id] = HeistMap.Parse(t);
        }
    }

    /// <summary>The templates of a kind and tier, in id order.</summary>
    public List<HeistTemplateDef> Of(HeistKind kind, int tier) => [.. _templates.Where(t => HeistMap.ParseKind(t.Kind) == kind && t.Tier == tier)];

    /// <summary>The template a site uses: the kind's templates of its tier (the nearest tier when there are none), picked by the site id.</summary>
    public HeistTemplateDef? Pick(HeistKind kind, int tier, ulong siteId)
    {
        for (int d = 0; d <= 2; d++)
            foreach (int t in d == 0 ? new[] { tier } : new[] { tier - d, tier + d })
            {
                var l = Of(kind, t);
                if (l.Count > 0) return l[(int)(DetRng.Mix(siteId ^ 0x6B00UL) % (ulong)l.Count)];
            }
        return null;
    }
}
