using Ravensguard.Content;

namespace Ravensguard.Sim.Siege;

/// <summary>What an engine is (siege/engines.jsonc <c>kind</c>).</summary>
public enum EngineKind : byte { Mantlet, Ram, Tower, Trebuchet, Springald }

/// <summary>The engine templates of the loaded content (<c>siege/engines.jsonc</c>) by vehicle template id, and the rules. Pure; no state.</summary>
public sealed class EngineTable
{
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<ContentDatabase, EngineTable> Cache = new();
    public static EngineTable Of(ContentDatabase db) => Cache.GetValue(db, d => new EngineTable(d));

    private readonly Dictionary<string, EngineStatDef> _stats = new(StringComparer.Ordinal);
    private readonly Dictionary<string, EngineKind> _kinds = new(StringComparer.Ordinal);
    public EngineRulesDef Rules { get; }

    public EngineTable(ContentDatabase db)
    {
        Rules = db.All<EngineRulesDef>().OrderBy(r => r.Id, StringComparer.Ordinal).FirstOrDefault() ?? new EngineRulesDef { Id = "engine_rules" };
        foreach (var s in db.All<EngineStatDef>().OrderBy(s => s.Id, StringComparer.Ordinal)) { _stats[s.Id] = s; _kinds[s.Id] = KindOf(s.Kind); }
    }

    public static EngineKind KindOf(string kind) => kind switch
    {
        "mantlet" => EngineKind.Mantlet, "ram" => EngineKind.Ram, "tower" => EngineKind.Tower, "trebuchet" => EngineKind.Trebuchet, "springald" => EngineKind.Springald,
        _ => throw new ArgumentException("unknown engine kind " + kind),
    };

    public EngineStatDef? Stat(string? template) => template != null && _stats.TryGetValue(template, out var s) ? s : null;
    public EngineKind Kind(string template) => _kinds[template];
    public IReadOnlyCollection<EngineStatDef> All => _stats.Values;

    /// <summary>Whether engines of this kind shoot (a missile or a blow) rather than only stand, roll and give cover.</summary>
    public static bool Shoots(EngineKind k) => k is EngineKind.Ram or EngineKind.Trebuchet or EngineKind.Springald;

    /// <summary>A part that does not burn: one of the rules' fireproof tags (iron, the ballast).</summary>
    public bool Fireproof(IReadOnlyCollection<string> tags) { foreach (var t in Rules.Fireproof) if (tags.Contains(t)) return true; return false; }
}
