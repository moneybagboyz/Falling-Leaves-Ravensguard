using Ravensguard.Sim.Combat.Melee2;

namespace Ravensguard.Sim.Combat.Flavour;

[Flags]
public enum LogFilterFlags
{
    None = 0,
    /// <summary>Only events where the hero is the actor or the target.</summary>
    HeroOnly = 1,
    /// <summary>The hero's side: the hero, the squad, followers and allied soldiers.</summary>
    MySide = 2,
    /// <summary>Kills and serious wounds: severity 2 and more, falling, dying, routing, yielding, severing.</summary>
    Serious = 4,
    /// <summary>Misses and defences (off by default).</summary>
    Misses = 8,
    /// <summary>Treatment lines (waking, dressing).</summary>
    Treatment = 16,
    /// <summary>Only fights with beasts and monsters.</summary>
    BeastsOnly = 32,
}

/// <summary>The combat log's filters (feedback-flavour-log 4.2). The defaults are the spec's: in a fight the hero only and the serious; in a battle the hero's side and the kills.</summary>
public sealed class LogFilter
{
    public LogFilterFlags Flags { get; set; }
    public string Search { get; set; } = "";

    public static LogFilter ForFight() => new() { Flags = LogFilterFlags.HeroOnly | LogFilterFlags.Serious };
    public static LogFilter ForBattle() => new() { Flags = LogFilterFlags.MySide | LogFilterFlags.Serious };
    public static LogFilter Everything() => new() { Flags = LogFilterFlags.Misses | LogFilterFlags.Treatment };

    public bool Has(LogFilterFlags f) => (Flags & f) == f;

    public static bool IsDefence(FightEventKind k) => k is FightEventKind.Blocked or FightEventKind.Parried or FightEventKind.Dodged or FightEventKind.Whiff or FightEventKind.Missed
        or FightEventKind.Overpowered or FightEventKind.Feint or FightEventKind.ProjectileMiss or FightEventKind.OutOfAmmo or FightEventKind.Stopped or FightEventKind.Shot or FightEventKind.Throw;

    /// <summary>Does the filter show this line? (The free text search is applied by <see cref="Apply"/>, which formats only the lines that pass the rest.)</summary>
    public bool Accepts(FightLog log, in FightLine l)
    {
        bool touchesHero = log.Hero >= 0 && (l.Actor == log.Hero || l.Target == log.Hero);
        if (Has(LogFilterFlags.HeroOnly)) { if (!touchesHero) return false; }
        else if (Has(LogFilterFlags.MySide))
        {
            bool mine = log.HeroTeam >= 0 && (Team(log, l.Actor) == log.HeroTeam || Team(log, l.Target) == log.HeroTeam);
            if (!mine) return false;
        }
        if (Has(LogFilterFlags.BeastsOnly) && !(Beast(log, l.Actor) || Beast(log, l.Target))) return false;
        if (Has(LogFilterFlags.Serious)) return l.IsSerious;
        if (!Has(LogFilterFlags.Misses) && IsDefence(l.Kind)) return false;
        if (!Has(LogFilterFlags.Treatment) && l.Kind == FightEventKind.Wake) return false;
        return true;
    }

    static int Team(FightLog log, int id) => log.TryInfo(id, out var f) ? f.Team : -1;
    static bool Beast(FightLog log, int id) => log.TryInfo(id, out var f) && f.Kind != "human";

    /// <summary>The indexes of the lines that pass (and, with a search text, whose prose contains it).</summary>
    public List<int> Apply(FightLog log, Func<int, string>? textOf = null)
    {
        var res = new List<int>();
        for (int i = 0; i < log.Lines.Count; i++)
        {
            if (!Accepts(log, log.Lines[i])) continue;
            if (Search.Length > 0 && textOf != null && textOf(i).IndexOf(Search, StringComparison.OrdinalIgnoreCase) < 0) continue;
            res.Add(i);
        }
        return res;
    }

    /// <summary>A short code for the settings file ("hero,serious").</summary>
    public string Serialise() => string.Join(',', Enum.GetValues<LogFilterFlags>().Where(f => f != LogFilterFlags.None && (Flags & f) == f).Select(f => f.ToString().ToLowerInvariant()));

    public static LogFilter Parse(string s)
    {
        var f = new LogFilter();
        foreach (var part in s.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            if (Enum.TryParse<LogFilterFlags>(part, true, out var v)) f.Flags |= v;
        return f;
    }
}
