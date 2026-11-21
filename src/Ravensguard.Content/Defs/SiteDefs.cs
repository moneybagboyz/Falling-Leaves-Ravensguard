namespace Ravensguard.Content;

// Exploration and sites (docs/specs/phase4/exploration-and-sites.md, P4-H): site templates, underground room templates, loot tables, traps, the
// site constants, the site monsters and the weather-in-combat table. Units are in the names: _pm permille, _m metres, _cm centimetres,
// _s sim-seconds (one sim-second is 20 live ticks at 1x, 20 game seconds), _mites coin, _milli momentum milli-units.

/// <summary>A weighted room choice of a site template.</summary>
public sealed record SiteRoomWeight
{
    [ContentRef(typeof(SiteRoomDef))] public required string Room { get; init; }
    public int Weight { get; init; } = 10;
}

/// <summary>Which rooms a template's levels are made of (3.1): always the required ones, then weighted picks until the count is reached.</summary>
public sealed record SiteRoomsDef
{
    [ContentRef(typeof(SiteRoomDef))] public List<string> Required { get; init; } = [];
    public List<SiteRoomWeight> Weighted { get; init; } = [];
    public int Min { get; init; } = 4;
    public int Max { get; init; } = 10;
    /// <summary>The room the deepest level of a monster lair gets (the burial chamber with the biers, the beast's den).</summary>
    [ContentRef(typeof(SiteRoomDef))] public string? Lair { get; init; }
}

/// <summary>The loot budget from history (4.2): <c>pop_peak x base x survive/1000 x max(floor, 1000 - decay x years)/1000</c> mites, drawn from the tables.</summary>
public sealed record SiteLootRuleDef
{
    public int BaseMitesPerPop { get; init; } = 150;
    public int SurvivePm { get; init; } = 150;
    public int DecayPmPerYear { get; init; } = 10;
    public int DecayFloorPm { get; init; } = 100;
    /// <summary>A flat budget for sites without a population history (caves, mines, barrows), mites.</summary>
    public int FlatMites { get; init; }
    [ContentRef(typeof(SiteLootTableDef))] public List<string> Tables { get; init; } = [];
}

public sealed record SiteInhabitantRoleDef
{
    /// <summary>scavengers, wolves, hermit, bandits, smugglers, miners, vermin, bear (a fight_kit row names each).</summary>
    public required string Role { get; init; }
    public int ChancePm { get; init; } = 1000;
    public List<int> Count { get; init; } = [1, 1];
}

public sealed record SiteHazardRollDef
{
    /// <summary>stale_air, foul_air, collapse, flood.</summary>
    public required string Id { get; init; }
    public int ChancePm { get; init; } = 100;
    /// <summary>How many zones (min, max) when rolled.</summary>
    public List<int> Zones { get; init; } = [1, 1];
    /// <summary>Instability of a collapse zone (permille, min and max), or the air index of a bad-air core.</summary>
    public List<int> Value { get; init; } = [];
}

public sealed record SiteTrapRollDef
{
    [ContentRef(typeof(SiteTrapDef))] public required string Id { get; init; }
    /// <summary>One trap per this many rooms (rounded up), at the rooms' trap anchors first.</summary>
    public int PerRooms { get; init; } = 8;
    /// <summary>One per entrance (the hideout's tripwires) instead of per rooms.</summary>
    public bool PerEntrance { get; init; }
}

/// <summary>One site template per (kind, sub-kind) (3.1).</summary>
[ContentType("site_template")]
public sealed record SiteTemplateDef : ContentDef
{
    /// <summary>Ruin, Cave, Mine, Barrow, Battlefield, Hoard (the Phase 1 <c>SiteKind</c> names).</summary>
    public required string Kind { get; init; }
    /// <summary>The sub-kind ("sacked", "den", "flooded", "barrow"; "*" for any); "lair" matches a monster lair.</summary>
    public string Sub { get; init; } = "*";
    /// <summary>rooms (binary partition, room-and-corridor), cave (cellular automaton), mine (adits and stopes), surface (battlefields, hoards).</summary>
    public string Layout { get; init; } = "rooms";
    public List<int> Levels { get; init; } = [1, 2];
    public List<int> SizeTiles { get; init; } = [48, 64];
    /// <summary>Wall material of the corridors: masonry for crypts and ruins, rock for caves and mines.</summary>
    public string Walls { get; init; } = "masonry";
    public SiteRoomsDef Rooms { get; init; } = new();
    public SiteLootRuleDef Loot { get; init; } = new();
    public List<SiteInhabitantRoleDef> Inhabitants { get; init; } = [];
    public List<SiteHazardRollDef> Hazards { get; init; } = [];
    public List<SiteTrapRollDef> Traps { get; init; } = [];
    /// <summary>Robbing it seeds a <c>taboo_robbed</c> chronicle event (barrows, crypts).</summary>
    public bool Taboo { get; init; }
    /// <summary>Old natural caves 100-300, rotten props 400-700, fresh mines 50 (permille): the instability of the long spans (4.5.3).</summary>
    public List<int> Instability { get; init; } = [0, 0];
}

/// <summary>
/// An underground room template (3.1, about 30 of them; <c>content/base/blueprints/underground/</c>), as text in the Phase 1 rows format. Characters:
/// <c>#</c> wall, <c>.</c> floor, <c>+</c> door (the room's connection to a corridor), <c>&lt;</c> stairs up, <c>&gt;</c> stairs down, <c>H</c> ladder down (a shaft),
/// <c>o</c> pillar or prop (a support), <c>$</c> a loot spot (chest, niche, stash), <c>n</c> a burial niche (named dead), <c>b</c> a bier or bed (a monster or sleeper),
/// <c>^</c> a trap spot, <c>l</c> a light sconce, <c>~</c> water (sump), <c>:</c> rubble, <c>v</c> ore face. Purposes say what the room is for (rest, storage, burial, guard, den, dock...).
/// </summary>
[ContentType("site_room")]
public sealed record SiteRoomDef : ContentDef
{
    public required List<string> Rows { get; init; }
    public List<string> Purposes { get; init; } = [];
    /// <summary>Site kinds that may use the room (empty: any).</summary>
    public List<string> Kinds { get; init; } = [];
    /// <summary>Wall material: masonry or rock.</summary>
    public string Walls { get; init; } = "masonry";
    /// <summary>The air of a sealed chamber (index 0..1000, 1000 fresh; 0: the level's air).</summary>
    public int SealedAir { get; init; }
}

public sealed record SiteLootEntryDef
{
    /// <summary>An item shape (a weapon, armour or a site item row) and its material.</summary>
    public required string Item { get; init; }
    public string Material { get; init; } = "";
    public int ValueMites { get; init; } = 100;
    public int Weight { get; init; } = 10;
    public int MassG { get; init; } = 1000;
    /// <summary>Condition range permille (rusted weapons 300-600, battlefield 200-500).</summary>
    public List<int> Condition { get; init; } = [1000, 1000];
    /// <summary>A named piece: takes the history's name (the reliquary of the abbey sacked in 287).</summary>
    public bool Named { get; init; }
    /// <summary>Rusted blades carry the infection flag (x2, resolver 4.7).</summary>
    public bool Dirty { get; init; }
}

/// <summary>A loot table (4.2: <c>sites/loot_tables.jsonc</c>): items are drawn by weight until the budget is spent.</summary>
[ContentType("site_loot_table")]
public sealed record SiteLootTableDef : ContentDef
{
    public required List<SiteLootEntryDef> Entries { get; init; }
}

/// <summary>A thing found underground that is not a weapon or armour (a reliquary, a torc, a chalice, coins of the dead): value, mass, a name.</summary>
[ContentType("site_item")]
public sealed record SiteItemDef : ContentDef
{
    public required string Name { get; init; }
    public int MassG { get; init; } = 500;
    public int ValueMites { get; init; } = 100;
    public string Kind { get; init; } = "valuable";
    /// <summary>What it is made of (a material id): the one part of the item row it becomes in the pack ("" falls back to bone).</summary>
    public string Material { get; init; } = "";
}

public sealed record SitePartChanceDef
{
    public required string Part { get; init; }
    public int Pm { get; init; } = 1000;
}

/// <summary>A trap (4.6, <c>sites/traps.jsonc</c>).</summary>
[ContentType("site_trap")]
public sealed record SiteTrapDef : ContentDef
{
    /// <summary>blow (a blunt or point blow through the resolver), fall (a pit: a fall of <see cref="FallMm"/>), floor (collapsing floor: all in 3 x 3 drop a level), alarm, hold (bear trap).</summary>
    public required string Effect { get; init; }
    /// <summary>blunt, point or edge for a blow.</summary>
    public string Attack { get; init; } = "blunt";
    public long MomentumMilli { get; init; }
    /// <summary>Contact area, mm2 (the resolver's attack area).</summary>
    public int AreaMm2 { get; init; } = 100;
    public int PenMm { get; init; }
    public int FallMm { get; init; } = 3000;
    /// <summary>A spiked pit: 1-3 point blows of this momentum on landing.</summary>
    public long SpikeMilli { get; init; }
    public List<SitePartChanceDef> Parts { get; init; } = [];
    public int Detect { get; init; } = 30;
    /// <summary>Disarm difficulty; 0: cannot be disarmed, only avoided (a collapsing floor).</summary>
    public int Disarm { get; init; } = 30;
    public int AlarmM { get; init; }
    /// <summary>A held foot: sim-seconds of Athletics to open (bear trap 20).</summary>
    public int HoldS { get; init; }
    /// <summary>Site kinds it is placed in.</summary>
    public List<string> Where { get; init; } = [];
}

/// <summary>A rare monster's behaviour outside the fight (4.9): its fight kit, senses, speed, leash and sleep.</summary>
[ContentType("site_monster")]
public sealed record SiteMonsterDef : ContentDef
{
    [ContentRef(typeof(FightKitDef))] public required string Kit { get; init; }
    public int WalkMmps { get; init; } = 2000;
    public int LungeMmps { get; init; } = 3500;
    public int LungeS { get; init; } = 5;
    public int RestS { get; init; } = 20;
    /// <summary>Sees in the dark this far, light or none (draugr 12 m); 0: by light like a person.</summary>
    public int DarkSightM { get; init; }
    /// <summary>Hunts by sound: the radii of walking, running, sneaking and fight noise it hears (cave beast 15, 25, 8, 30).</summary>
    public int HearWalkM { get; init; }
    public int HearRunM { get; init; }
    public int HearSneakM { get; init; }
    public int HearFightM { get; init; }
    /// <summary>Sleepers' noise heard at this range while asleep (8 m).</summary>
    public int HearAsleepM { get; init; }
    /// <summary>Dormant on a bier until woken (draugr).</summary>
    public bool Dormant { get; init; }
    /// <summary>Wake chance permille per sim-second once a trigger holds (10%).</summary>
    public int WakePmPerS { get; init; } = 100;
    /// <summary>Wake triggers: combat noise within this many metres, a living person this many sim-seconds in the chamber.</summary>
    public int WakeNoiseM { get; init; } = 20;
    public int WakeStayS { get; init; } = 60;
    /// <summary>Follows out of the lair up to this far from the mouth (100 m), returns after this many sim-seconds without a living target within <see cref="LoseM"/>.</summary>
    public int LeashM { get; init; } = 100;
    public int ReturnAfterS { get; init; } = 120;
    public int LoseM { get; init; } = 60;
    /// <summary>Asleep from this local hour to <see cref="WakeHour"/> (cave beast 6 to 22); -1: never sleeps.</summary>
    public int SleepHour { get; init; } = -1;
    public int WakeHour { get; init; } = -1;
    public int InvestigateS { get; init; } = 20;
    /// <summary>Attacks anything within this range once awake.</summary>
    public int AttackM { get; init; } = 8;
    /// <summary>Flees to the lair below this blood (permille lost), recovers this much blood permille per game hour there.</summary>
    public int FleeLossPm { get; init; } = 1000;
    public int RecoverPmPerHour { get; init; } = 10;
    /// <summary>Never flees, never yields, no stamina limit (the draugr).</summary>
    public bool Tireless { get; init; }
    /// <summary>Fire burns it (the draugr's head through in about 30 sim-seconds).</summary>
    public bool Burns { get; init; }
    /// <summary>The chronicle line key and the renown a slayer earns.</summary>
    public string SlainKey { get; init; } = "";
    public int Renown { get; init; } = 10;
}

/// <summary>The numbers of exploration (3.3, <c>sites/constants.jsonc</c>, one row "default").</summary>
[ContentType("site_constants")]
public sealed record SiteConstantsDef : ContentDef
{
    public int TorchBurnS { get; init; } = 1200;
    public int TorchRadius { get; init; } = 6;
    public int TorchIntensity { get; init; } = 235;
    public int CandleBurnS { get; init; } = 1800;
    public int CandleRadius { get; init; } = 3;
    public int CandleIntensity { get; init; } = 140;
    /// <summary>Lantern: 3,600 sim-s per 0.3 l of oil, radius 8, intensity 245, rain-proof.</summary>
    public int LanternBurnS { get; init; } = 3600;
    public int LanternRadius { get; init; } = 8;
    public int LanternIntensity { get; init; } = 245;
    public int GutterS { get; init; } = 60;
    public int GutterRadiusLoss { get; init; } = 3;
    public int GutterIntensity { get; init; } = 120;
    public int RainLightBurnPm { get; init; } = 1500;
    public int RainHeavyBurnPm { get; init; } = 2500;
    /// <summary>Chance per sim-second a torch goes out, x10 permille (5 = 0.5%).</summary>
    public int RainHeavyOutPmX10 { get; init; } = 5;
    public int WindOutPmX10 { get; init; } = 2;
    public int WindOutMps { get; init; } = 12;
    /// <summary>Air index thresholds (4.5.1): >= 800 none, 600-799 candle gutters, 400-599 torches x0.5, 250-399 dazed, below 250 unconscious.</summary>
    public int AirGutter { get; init; } = 800;
    public int AirDim { get; init; } = 600;
    public int AirDazed { get; init; } = 400;
    public int AirFoul { get; init; } = 250;
    public int AirUnconsciousS { get; init; } = 20;
    public int AirDeathS { get; init; } = 120;
    /// <summary>Air rises this much per tile away from a bad-air core (so the gutter warning comes 30 m before the foul core: EX-18).</summary>
    public int AirSlopePerTile { get; init; } = 18;
    public int AirVentTiles { get; init; } = 6;
    public int AirVentBonus { get; init; } = 200;
    public int FloodRiseCmPerHour { get; init; } = 30;
    public int FloodHours { get; init; } = 6;
    public int FloodPm { get; init; } = 40;
    public int FloodRainHours { get; init; } = 4;
    public int WadeSlowCm { get; init; } = 30;
    public int SwimCm { get; init; } = 90;
    public int SwimMmps { get; init; } = 1000;
    public int HoldBreathS { get; init; } = 30;
    public int SinkGearKg { get; init; } = 15;
    public int SinkCheckS { get; init; } = 5;
    public int SinkCheckPm { get; init; } = 500;
    public int CaveInRadiusMin { get; init; } = 2;
    public int CaveInRadiusMax { get; init; } = 3;
    public int CaveInWarnS { get; init; } = 6;
    public int CaveInWindowS { get; init; } = 60;
    public long CaveInBlowMilli { get; init; } = 150_000;
    public int CaveInDodgePm { get; init; } = 800;
    public int DigS { get; init; } = 20;
    public int ReoccupyAfterDays { get; init; } = 20;
    public int ReoccupyPmPerDay { get; init; } = 20;
    public int ReoccupyPressure { get; init; } = 500;
    public int ScavengeSitesPm { get; init; } = 100;
    public int ScavengeLossPm { get; init; } = 50;
    public int FledReturnDays { get; init; } = 3;
    public int TrapNearMm { get; init; } = 2500;
    public int DisarmS { get; init; } = 6;
    public int DisarmFailTriggerPm { get; init; } = 250;
    public int SneakSkipPm { get; init; } = 200;
    public int SneakSkipMaxKg { get; init; } = 60;
    public int MapShareM { get; init; } = 20;
    public int BreadcrumbCells { get; init; } = 400;
    public int LostLight { get; init; } = 20;
    public int SeenLight { get; init; } = 30;
    public int UndergroundTempC { get; init; } = 8;
    public int RatsPmPerDay { get; init; } = 20;
    public int SuperstitiousMoraleX100 { get; init; } = 500;
    public int BattlefieldSearchS { get; init; } = 60;
    public int BattlefieldDecayPmPerYear { get; init; } = 8;
    public int HoardDigS { get; init; } = 120;
    public int RestAir { get; init; } = 600;
    /// <summary>Metres per level of depth in the leash and the exit time (a flight of stairs).</summary>
    public int LevelDepthM { get; init; } = 3;
    /// <summary>The hero's carry capacity: 15 kg + 2.5 kg per strength point above none (existing rule).</summary>
    public int CarryBaseKg { get; init; } = 15;
}

/// <summary>One row of the weather-in-combat table (4.8, <c>combat/weather_mods.jsonc</c>), read by the fight's input builder, the ranged layer, the horses and the stamina model. Outdoors only.</summary>
[ContentType("weather_combat")]
public sealed record WeatherCombatDef : ContentDef
{
    /// <summary>The <c>WeatherState</c> name.</summary>
    public required string State { get; init; }
    public int SigmaPm { get; init; } = 1000;
    /// <summary>The sigma applies only beyond this range (fog: 15 m); 0: at any range.</summary>
    public int SigmaBeyondM { get; init; }
    public int VisionCapM { get; init; }
    public int DodgeCooldownTicks { get; init; }
    public int EvadePoints { get; init; }
    public int SpeedPm { get; init; } = 1000;
    public int CavalrySpeedPm { get; init; } = 1000;
    public bool NoCharges { get; init; }
    public int TorchBurnPm { get; init; } = 1000;
    public int TorchOutPmX10 { get; init; }
    public bool FiresOut { get; init; }
    public int HearingPm { get; init; } = 1000;
    public int BowRangePm { get; init; } = 1000;
    public int StaminaRegenPm { get; init; } = 1000;
    /// <summary>In armour over this many kg the swing costs this much more stamina and regenerates at <see cref="HeavyRegenPm"/>.</summary>
    public int HeavyArmourKg { get; init; }
    public int HeavySwingCostPm { get; init; } = 1000;
    public int HeavyRegenPm { get; init; } = 1000;
    public int SurprisePoints { get; init; }
    public int StealthPoints { get; init; }
    /// <summary>Slip chance per sim-second, x10 permille: at a gallop (frost 80 = 8%) and for infantry sprinting (10 = 1%).</summary>
    public int SlipGallopPmX10 { get; init; }
    public int SlipSprintPmX10 { get; init; }
    public int GripPoints { get; init; }
    public int DayLightPm { get; init; } = 1000;
    public int SuperstitiousX100 { get; init; }
    public int HorsePanicPerFlash { get; init; }
}
