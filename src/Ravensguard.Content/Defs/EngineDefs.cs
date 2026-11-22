namespace Ravensguard.Content;

/// <summary>
/// P7-SG-B (sieges-and-engines 3.3, 4.2; content <c>content/base/siege/engines.jsonc</c>): what an engine template does besides being a vehicle. The id is the vehicle template's id (<c>trebuchet</c>, <c>ram_covered</c>).
/// Times are game seconds (a live tick is one game second, so the abstract and the live tier step the same numbers); speeds are the engine's own, never a second table: a pushed engine's speed is the cart formula with
/// the pushers' newtons as its pull.
/// </summary>
[ContentType("engine_stat")]
public sealed record EngineStatDef : ContentDef
{
    /// <summary><c>ram</c>, <c>mantlet</c>, <c>tower</c>, <c>trebuchet</c> or <c>springald</c>.</summary>
    public required string Kind { get; init; }
    /// <summary>People it takes to work it (swing the ram, wind the winch, load and loose the bolt).</summary>
    public int Crew { get; init; }
    /// <summary>People who push it (0: it is not pushed); <see cref="Carry"/> people who carry it (a springald), 0 if none.</summary>
    public int Push { get; init; }
    public int Carry { get; init; }
    /// <summary>The ammunition good one shot uses up (empty: none), and its mass in grams; the shot's mass and speed.</summary>
    public string Ammo { get; init; } = "";
    public int ShotG { get; init; }
    public int ShotMmps { get; init; }
    /// <summary>Aim error sigma in thousandths of a degree at Engineering 0, and the range error in permille of the range.</summary>
    public int SigmaMdeg { get; init; }
    public int RangeErrPm { get; init; }
    public int MinRangeM { get; init; }
    public int MaxRangeM { get; init; }
    /// <summary>Game seconds between shots (a ram's swing, a trebuchet's wind-up).</summary>
    public int ReloadS { get; init; }
    /// <summary>A ram's swing speed (mm/s) at the end of its arc; the beam's own mass is the part's.</summary>
    public int SwingMmps { get; init; }
    /// <summary>Permille of missiles from the front the engine's boards stop for its crew (0: none); read with the parts that are still standing.</summary>
    public int CoverPm { get; init; }
    /// <summary>A tower's floors, the level of its top floor above the ground (1: the wall-walk) and the reach of its drawbridge in mm.</summary>
    public int Floors { get; init; }
    public int TopZ { get; init; }
    public int BridgeMm { get; init; }
    /// <summary>People who can fight from a tower's upper floors.</summary>
    public int Archers { get; init; }
    /// <summary>The slowest an engine is pushed, in cm/s on firm level ground (the cart formula's base speed).</summary>
    public int BaseCmps { get; init; }
    /// <summary>A tower needs firm and level ground: the steepest slope it climbs, permille.</summary>
    public int MaxSlopePm { get; init; } = 1000;
    public bool NeedsFirm { get; init; }
}

/// <summary>P7-SG-B (sieges-and-engines 4.2): the numbers of the engine rules, in content.</summary>
[ContentType("engine_rules")]
public sealed record EngineRulesDef : ContentDef
{
    /// <summary>Aim and range error shrink by <c>(1 - Engineering / this)</c> (spec: 150).</summary>
    public int SkillDivisor { get; init; } = 150;
    /// <summary>The hours of the day engines shoot in the abstract tier (12 shooting hours a day).</summary>
    public int ShootFromHour { get; init; } = 6;
    public int ShootToHour { get; init; } = 18;
    /// <summary>A ram with an iron head built on it swings with this share of the beam's momentum (permille; spec: x1.2).</summary>
    public int IronHeadPm { get; init; } = 1200;
    /// <summary>People hit by a shot's landing: everyone within this many mm of it.</summary>
    public int SplashMm { get; init; } = 1500;
    /// <summary>A blow on an engine's part: momentum (kg m/s) under the threshold does nothing, each one over it takes this many hit points.</summary>
    public int PartThresholdKgMs { get; init; } = 20;
    public int PartHpPerKgMs { get; init; } = 5;
    /// <summary>Fire on an engine (4.2): a burning pot or a torch lights a flammable part with this chance (permille); a part under wet hides, this share of it; hides dry in this many game hours; a burning part loses this many hit points a game minute.</summary>
    public int IgnitePm { get; init; } = 600;
    public int WetHidesPm { get; init; } = 100;
    public int HidesDryHours { get; init; } = 6;
    public int BurnHpPerMin { get; init; } = 40;
    /// <summary>Tags of parts that do not burn.</summary>
    public List<string> Fireproof { get; init; } = [];
}
