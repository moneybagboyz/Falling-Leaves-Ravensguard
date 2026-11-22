using MessagePack;
using Ravensguard.Content;
using Ravensguard.Sim.Core;

namespace Ravensguard.Sim.Siege;

/// <summary>
/// P7-SG-A (sieges-and-engines 3.1): the one damage rule. A blow of momentum M kg m/s on a wall-like piece with impact threshold T does <c>max(0, M - T) / 60</c> hit points (integer division). It is the same for a ram's swing, a
/// trebuchet's stone, a pick and a falling tower; there is no default damage. A blow that comes out of the resolver carries its momentum in milli-units (<see cref="ImpactMilli"/>).
/// </summary>
public static class WallDamage
{
    public static int Impact(long momentumKgMs, int threshold, int divisor = 60)
        => momentumKgMs <= threshold || divisor <= 0 ? 0 : (int)Math.Min(int.MaxValue, (momentumKgMs - threshold) / divisor);

    /// <summary>The same for a momentum in milli kg m/s (the resolver's unit): the whole kg m/s is what the threshold is compared with.</summary>
    public static int ImpactMilli(long momentumMilli, int threshold, int divisor = 60) => Impact(momentumMilli / 1000, threshold, divisor);
}

/// <summary>The wall pieces of the loaded content (<c>siege/walls.jsonc</c>): a lookup of the threshold and the rubble by piece id, and the rules. Pure; no state.</summary>
public sealed class WallTable
{
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<ContentDatabase, WallTable> Cache = new();
    public static WallTable Of(ContentDatabase db) => Cache.GetValue(db, d => new WallTable(d));

    private readonly Dictionary<string, WallStatDef> _stats = new(StringComparer.Ordinal);
    private readonly HashSet<string> _rubble = new(StringComparer.Ordinal);
    public SiegeRulesDef Rules { get; }

    public WallTable(ContentDatabase db)
    {
        Rules = db.All<SiegeRulesDef>().OrderBy(r => r.Id, StringComparer.Ordinal).FirstOrDefault() ?? new SiegeRulesDef { Id = "siege_rules" };
        foreach (var s in db.All<WallStatDef>().OrderBy(s => s.Id, StringComparer.Ordinal)) { _stats[s.Id] = s; if (s.Rubble.Length > 0) _rubble.Add(s.Rubble); }
    }

    /// <summary>Momentum over the threshold per hit point for a piece: its own divisor, else the rules'.</summary>
    public int DivisorOf(WallStatDef stat) => stat.DamageDivisor > 0 ? stat.DamageDivisor : Rules.DamageDivisor;

    public WallStatDef? Stat(string? piece) => piece != null && _stats.TryGetValue(piece, out var s) ? s : null;
    public IReadOnlyCollection<WallStatDef> All => _stats.Values;
    /// <summary>Whether the piece is what a wall leaves at ground level.</summary>
    public bool IsRubble(string? piece) => piece != null && _rubble.Contains(piece);
}

/// <summary>One cell's entry in the chunk's <c>siege_damage</c> object: the hit points lost, the piece that stood there (by id, so a renumbered palette changes nothing), and whether it is down (rubble, or gone).</summary>
[MessagePackObject]
public sealed class WallHit
{
    /// <summary>The cell's local index in its chunk (y x 32 + x); the chunk key carries the level.</summary>
    [Key(0)] public int Idx { get; set; }
    [Key(1)] public int Lost { get; set; }
    [Key(2)] public string Piece { get; set; } = "";
    [Key(3)] public bool Down { get; set; }
}

/// <summary>The value of the <c>siege_damage</c> property: the damaged and downed cells of one chunk level, sorted by index (a canonical encoding, so a save-load-save is byte-identical).</summary>
[MessagePackObject]
public sealed class WallHits
{
    [Key(0)] public List<WallHit> Cells { get; set; } = [];
    public WallHit? Find(int idx) { foreach (var c in Cells) if (c.Idx == idx) return c; return null; }
    public void Put(WallHit h)
    {
        for (int i = 0; i < Cells.Count; i++) if (Cells[i].Idx == h.Idx) { Cells[i] = h; return; }
        Cells.Add(h); Cells.Sort((a, b) => a.Idx.CompareTo(b.Idx));
    }
    public void Remove(int idx) => Cells.RemoveAll(c => c.Idx == idx);
    public WallHits Clone() => new() { Cells = [.. Cells.Select(c => new WallHit { Idx = c.Idx, Lost = c.Lost, Piece = c.Piece, Down = c.Down })] };
}

public enum WallBlowKind : byte { NotAWall, NoEffect, Damaged, Destroyed }

/// <summary>The result of one blow on a cell: what it did, the cell's hit points after, and the cells that fell with it (the walk and parapet over a breach, a tower's floors).</summary>
public sealed record WallBlow(WallBlowKind Kind, int Damage, int HpLeft, IReadOnlyList<(int X, int Y, int Z)> Fell)
{
    public static readonly WallBlow Not = new(WallBlowKind.NotAWall, 0, 0, []);
}

/// <summary>A breach: wall cells torn through the wall's whole thickness, at least the rules' width (2 m) wide. <paramref name="Cells"/> are the ground-level cells (x, y), sorted; <paramref name="Along"/> the axis the wall runs along.</summary>
public sealed record Breach(IReadOnlyList<(int X, int Y)> Cells, char Along, int WidthM, int ThicknessM)
{
    public bool Contains(int x, int y) { foreach (var c in Cells) if (c.X == x && c.Y == y) return true; return false; }
}

/// <summary>What mending a cell takes (<see cref="WallWorld.RepairNeed"/>): the piece, the share of it to rebuild in permille (a damaged cell needs part of it) and its worker milli-hours and materials scaled to that share. Labour and goods are the caller's.</summary>
public sealed record RepairNeed(string Piece, int SharePm, long MilliHours, IReadOnlyList<(string Good, long Grams)> Materials, bool Down);

/// <summary>Writes one chronicle entry (the outbox's keyed overload, or a test's chronicle) and returns its id.</summary>
public delegate EntityId SiegeChronicle(GameTime when, string typeId, string textKey, IReadOnlyList<ChronParam> parameters, IReadOnlyList<EntityId> subjects, IReadOnlyList<Cause> causes, (string Key, string Value)[] data);
