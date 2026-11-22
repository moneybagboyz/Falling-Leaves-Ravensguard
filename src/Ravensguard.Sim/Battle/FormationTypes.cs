using System.Runtime.CompilerServices;
using Ravensguard.Content;
using Ravensguard.Sim.Combat.Watch;

namespace Ravensguard.Sim.Battle;

/// <summary>What a formation is (battle-bubble-and-formations 3.2). Append only: saved in the formation rows.</summary>
public enum FormationKind : byte { Infantry, Archers, Crossbows, Cavalry, Retinue, Allied, Skirmishers, EngineCrew, WagonFort }

/// <summary>The orders a formation takes (3.3). Append only. <see cref="None"/> is the empty order.</summary>
public enum OrderKind : byte
{
    None, MoveTo, Advance, Charge, Hold, FollowMe, FlankLeft, FlankRight, ShieldWall, Loose, Line, Column, SetAgainstCavalry, FireAtWill, HoldFire, AttackFormation, Retreat, Rally, Face, Wheel,
    /// <summary>The last two are the cavalry spec's (they are accepted and recorded; the mounted layer acts on them).</summary>
    Dismount, Mount,
    /// <summary>P7-CV-A: a cavalry formation takes the wedge (a momentary shape order, like Line).</summary>
    Wedge,
}

/// <summary>How an order reached its captain (4.5).</summary>
public enum OrderSignal : byte { Voice, Horn, Runner, Captain }

/// <summary>Whether a formation's missile troops shoot at will, hold their fire, or shoot only at the formation ordered (3.2).</summary>
public enum FireMode : byte { AtWill, Hold, AtTarget }

/// <summary>
/// One order (3.3): a kind, where and at whom, the facing wanted, when it was issued and when it reaches the formation's captain, who gave it and how it travelled. Integers only; <see cref="Pack"/>
/// is its saved form.
/// </summary>
public struct FormationOrder
{
    public OrderKind Kind;
    public int TargetX, TargetY;
    /// <summary>The target formation's id on the enemy side (0: none), or the friendly formation a follow order names.</summary>
    public int TargetFormation;
    /// <summary>The fighter a follow-me order follows (-1: none).</summary>
    public int TargetFighter;
    /// <summary>The facing wanted, a Q10 vector (0, 0: none).</summary>
    public int FaceX, FaceY;
    /// <summary>The frontage in mm a drawn order asks for (0: unchanged).</summary>
    public int Frontage;
    /// <summary>A wheel's pivot side: positive about the right flank, otherwise the left.</summary>
    public int Arg;
    public int IssuedTick, ArriveTick;
    /// <summary>The fighter who gave it (-1: the player's own hand or a captain).</summary>
    public int By;
    public OrderSignal Signal;
    /// <summary>The runner who carries it (-1: none); if he falls before it arrives the order is lost.</summary>
    public int Runner;
    /// <summary>Queue it after the current order instead of replacing it.</summary>
    public bool Queued;
    public int Serial;

    public static FormationOrder Of(OrderKind k) => new() { Kind = k, TargetFormation = 0, TargetFighter = -1, By = -1, Runner = -1 };

    public const int PackLen = 16;

    public readonly void Pack(List<long> o)
    {
        o.Add((long)Kind | (long)Signal << 8 | (Queued ? 1L << 16 : 0)); o.Add(TargetX); o.Add(TargetY); o.Add(TargetFormation); o.Add(TargetFighter); o.Add(FaceX); o.Add(FaceY);
        o.Add(Frontage); o.Add(Arg); o.Add(IssuedTick); o.Add(ArriveTick); o.Add(By); o.Add(Runner); o.Add(Serial); o.Add(0); o.Add(0);
    }

    public static FormationOrder Unpack(ReadOnlySpan<long> s) => new()
    {
        Kind = (OrderKind)(byte)s[0], Signal = (OrderSignal)(byte)(s[0] >> 8), Queued = (s[0] & 1L << 16) != 0, TargetX = (int)s[1], TargetY = (int)s[2], TargetFormation = (int)s[3], TargetFighter = (int)s[4],
        FaceX = (int)s[5], FaceY = (int)s[6], Frontage = (int)s[7], Arg = (int)s[8], IssuedTick = (int)s[9], ArriveTick = (int)s[10], By = (int)s[11], Runner = (int)s[12], Serial = (int)s[13],
    };
}

/// <summary>The formation layer's content (<c>battle/formations.jsonc</c>, <c>orders.jsonc</c>, <c>captains.jsonc</c>) read once per content database; a missing row falls back to the spec's numbers.</summary>
public sealed class FormationRules
{
    public BattleRulesDef R { get; }
    /// <summary>The cavalry layer's constants (<c>battle/cavalry.jsonc</c>).</summary>
    public CavalryRulesDef Cav { get; }
    private readonly Dictionary<string, FormationShapeDef> _shapes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, BattleOrderDef> _orders = new(StringComparer.Ordinal);
    private readonly List<CaptainRuleDef> _captain;

    public FormationRules(ContentDatabase db)
    {
        R = db.All<BattleRulesDef>().OrderBy(x => x.Id, StringComparer.Ordinal).FirstOrDefault() ?? new BattleRulesDef { Id = "default" };
        Cav = db.All<CavalryRulesDef>().OrderBy(x => x.Id, StringComparer.Ordinal).FirstOrDefault() ?? new CavalryRulesDef { Id = "default" };
        foreach (var s in db.All<FormationShapeDef>()) _shapes[s.Id] = s;
        foreach (var o in db.All<BattleOrderDef>()) _orders[o.Id] = o;
        _captain = [.. db.All<CaptainRuleDef>().OrderBy(c => c.Tactics).ThenBy(c => c.Id, StringComparer.Ordinal)];
    }

    private static readonly ConditionalWeakTable<ContentDatabase, FormationRules> Cache = new();
    public static FormationRules For(ContentDatabase db) => Cache.GetValue(db, d => new FormationRules(d));

    public static string Snake(string name)
    {
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < name.Length; i++) { if (i > 0 && char.IsUpper(name[i])) sb.Append('_'); sb.Append(char.ToLowerInvariant(name[i])); }
        return sb.ToString();
    }

    private static readonly FormationShapeDef Fallback = new() { Id = "line" };

    public FormationShapeDef Shape(Formation f) => _shapes.TryGetValue(Snake(f.ToString()), out var s) ? s : _shapes.GetValueOrDefault("line") ?? Fallback;

    /// <summary>The shape's row for a formation of this kind: a cavalry formation reads its own spacing (<c>cavalry_line</c>, <c>cavalry_loose</c>, <c>cavalry_column</c>; the wedge has one row), anyone else the shared row.</summary>
    public FormationShapeDef Shape(Formation f, FormationKind kind)
    {
        if (kind == FormationKind.Cavalry && _shapes.TryGetValue("cavalry_" + Snake(f.ToString()), out var c)) return c;
        return Shape(f);
    }

    public BattleOrderDef Order(OrderKind k) => _orders.TryGetValue(Snake(k.ToString()), out var o) ? o : new BattleOrderDef { Id = Snake(k.ToString()) };

    /// <summary>Whether a captain of this Tactics may use the rule (a rule row at or below his Tactics). A rule with no row is never allowed.</summary>
    public bool CaptainMay(string rule, int tactics)
    {
        foreach (var c in _captain) if (c.Rule == rule) return tactics >= c.Tactics;
        return false;
    }
}
