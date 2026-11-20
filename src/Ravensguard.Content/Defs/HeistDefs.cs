namespace Ravensguard.Content;

/// <summary>
/// One heist site template (heists-and-jailbreaks 3, <c>blueprints/underground/heist.jsonc</c>): a one-level map in metre cells. Glyphs: <c>#</c> wall, space rock, <c>.</c> floor, <c>+</c> a door with no lock,
/// <c>1</c> to <c>5</c> a locked door of quality Crude to Masterwork, <c>c</c> a cell door (bars, quality <see cref="CellLock"/>), <c>g</c> a drain grate, <c>~</c> culvert (floor under the street, dark),
/// <c>s</c> a sconce (a floor cell with a wall light), <c>h</c> a hiding spot (behind casks, under stairs), <c>b</c> a bed (a sleeper's), <c>P</c> a prisoner's slot, <c>G</c> a guard's post, <c>A B C E F H J K</c> the patrol
/// waypoints in order, <c>T</c> the chest (the treasury or the guild's), <c>x</c> the way in from the street, <c>u</c> and <c>D</c> the two ends of the drain that joins a gaol (a portal to the partner site).
/// CI: every row the same width, every floor reachable from the way in or the drain, a known kind.
/// </summary>
[ContentType("heist_template")]
public sealed record HeistTemplateDef : ContentDef
{
    /// <summary>gaol, strongroom, vault, undercroft, drain or camp.</summary>
    public required string Kind { get; init; }
    /// <summary>0 a manor's cellar, 1 a town's, 2 a castle's.</summary>
    public int Tier { get; init; }
    public required List<string> Rows { get; init; }
    /// <summary>The quality (0 Crude .. 4 Masterwork) of the cell doors.</summary>
    public int CellLock { get; init; } = 1;
    /// <summary>The men of the day shift (the night shift has half).</summary>
    public int Guards { get; init; } = 4;
}

/// <summary>One lock quality (<c>heists/locks.jsonc</c>): the time a pick takes in sim-seconds, the skill offset against the Phase 4 lockpick rule, the time to break the door.</summary>
[ContentType("heist_lock")]
public sealed record HeistLockDef : ContentDef
{
    public int Quality { get; init; }
    public int PickS { get; init; } = 40;
    public int Offset { get; init; }
    public int BreakS { get; init; } = 20;
}

/// <summary>The numbers of the heist (heists-and-jailbreaks 3, 4): noise in metres in still air, the weather's and the walls' scale in percent, the guards' round, the bribe and disguise rules, the manhunt. The spec's defaults.</summary>
[ContentType("heist_settings")]
public sealed record HeistSettingsDef : ContentDef
{
    public int NoiseWalkM { get; init; } = 6;
    public int NoiseRunM { get; init; } = 15;
    public int NoiseSneakM { get; init; } = 2;
    public int NoiseDoorM { get; init; } = 8;
    public int NoisePickM { get; init; } = 4;
    public int NoiseBreakM { get; init; } = 25;
    public int NoiseFightM { get; init; } = 30;
    public int NoiseShoutM { get; init; } = 40;
    public int NoiseStoneM { get; init; } = 10;
    public int NoiseSearchM { get; init; } = 2;
    public int NoiseDouseM { get; init; } = 2;
    public int NoiseGrateM { get; init; } = 8;
    /// <summary>A quiet takedown of a guard who has not seen the hero (a scuffle, not a fight).</summary>
    public int NoiseSubdueM { get; init; } = 8;
    public int RainLightPct { get; init; } = 85;
    public int RainHeavyPct { get; init; } = 70;
    public int StormPct { get; init; } = 50;
    public int WallPct { get; init; } = 50;
    public int SleeperPct { get; init; } = 30;
    /// <summary>A guard looks and listens every this many seconds.</summary>
    public int LookEveryS { get; init; } = 5;
    public int InvestigateS { get; init; } = 20;
    public int LoopMinS { get; init; } = 360;
    public int LoopMaxS { get; init; } = 720;
    public int SearchS { get; init; } = 6;
    public int SearchWakePct { get; init; } = 10;
    public int DouseS { get; init; } = 2;
    public int GrateS { get; init; } = 30;
    public int SconceFromHour { get; init; } = 18;
    public int SconceToHour { get; init; } = 6;
    public int SleepFromHour { get; init; } = 2;
    /// <summary>A guard within this many metres of a hero in livery challenges him.</summary>
    public int DisguiseM { get; init; } = 4;
    /// <summary>Between DisguiseM and this many metres the challenge is a draw (stream 0x6B3).</summary>
    public int DisguiseBorderM { get; init; } = 5;
    public int DisguiseBorderPct { get; init; } = 50;
    public int ManhuntKmPerHour { get; init; } = 1;
    public int ManhuntHours { get; init; } = 24;
    public int ChangeLocksDays { get; init; } = 2;
    /// <summary>What a man can carry out of a chest: at most this many mites of coin, and this share (percent) of what the treasury holds.</summary>
    public int ChestCarryMites { get; init; } = 20_000;
    public int ChestSharePct { get; init; } = 40;
    public int FireRespondM { get; init; } = 20;
    public int FireWatchS { get; init; } = 120;
}
