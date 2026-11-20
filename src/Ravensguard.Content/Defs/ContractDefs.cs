namespace Ravensguard.Content;

/// <summary>
/// P5-D: the rules of building contracts and of background growth made visible (docs/specs/phase5/build-mode-and-construction.md 4.7, 4.9; content
/// <c>content/base/build/contracts.jsonc</c>). Money in mites, rates in permille, times in game days or hours as named.
/// </summary>
[ContentType("contract_rules")]
public sealed record ContractRulesDef : ContentDef
{
    // ---------- the bid (spec 4.7) ----------
    /// <summary>The builder's margin on labour (the price solver's 1,450 pm): labour = piece-hours x the wage class x this / 1000 / 10 hours a day.</summary>
    public int MarginPm { get; init; } = 1450;
    /// <summary>The crew a builder brings unless asked otherwise (BC-16: four).</summary>
    public int CrewDefault { get; init; } = 4;
    /// <summary>Builders are the region's carpentry and masonry businesses (business kind ids).</summary>
    public List<string> BuilderKinds { get; init; } = ["carpentry", "masonry"];

    /// <summary>The builder's cart brings his materials to the pile: a load per round trip (kg), its pace (m a minute, an ox cart) and the minutes to load and unload it.</summary>
    public int CartKg { get; init; } = 600;
    public int CartMetresPerMinute { get; init; } = 60;
    public int CartLoadMinutes { get; init; } = 15;

    // ---------- the stages (spec 4.7) ----------
    /// <summary>The share of the work (piece-hours) built at which the second stage falls due, permille.</summary>
    public int MidStagePm { get; init; } = 500;

    // ---------- the pace (spec 5: 0x511, +-10% a day) ----------
    /// <summary>A contractor's crew starts its day late by 0 to twice this permille of the working day (0x511, key contract and day): about 90% of the day, +-10%.</summary>
    public int PaceJitterPm { get; init; } = 100;

    // ---------- posted contracts (the lord's planner, spec 4.7) ----------
    /// <summary>A posted contract waits this many days on the board for the hero before a builder of the region takes it.</summary>
    public int NoticeDays { get; init; } = 2;
    /// <summary>A contract the hero takes must be finished within this many times its expected days (at least <see cref="MinDueDays"/>), or it goes back on the board.</summary>
    public int DueFactor { get; init; } = 3;
    public int MinDueDays { get; init; } = 20;

    // ---------- background growth made visible (spec 4.9) ----------
    /// <summary>Visible growth sites: the payer household's own hands and hired idle hands, at Phase 3's hours a day (growth tuning), the unskilled wage a day.</summary>
    public bool GrowthSites { get; init; } = true;
}
