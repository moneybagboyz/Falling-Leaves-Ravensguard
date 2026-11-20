namespace Ravensguard.Content;

/// <summary>
/// One illness (disease-and-plague 3, <c>medicine/illness.jsonc</c>): the id is the <c>IllnessKind</c> name in snake case. Days are game days; the death numbers are parts per million
/// for the one check at the course's worst day, untreated, nursed and with a physician. The effects are what the live tier reads (stamina, strength, sleep, water, noise).
/// </summary>
[ContentType("illness")]
public sealed record IllnessDef : ContentDef
{
    public int CourseDays { get; init; } = 5;
    /// <summary>The day of the course on which the one death check is made.</summary>
    public int WorstDay { get; init; } = 3;
    public int DeathUntreatedPpm { get; init; }
    public int DeathNursedPpm { get; init; }
    public int DeathPhysicianPpm { get; init; }
    /// <summary>A sick housemate adds to the household's onset chances.</summary>
    public bool Contagious { get; init; }
    /// <summary>Stamina regeneration lowered by this percent.</summary>
    public int StaminaRegenLossPct { get; init; }
    public int StrengthLoss { get; init; }
    public int SleepExtraHours { get; init; }
    /// <summary>Water need as a percent of normal.</summary>
    public int WaterNeedPct { get; init; } = 100;
    public bool BedBound { get; init; }
    /// <summary>Sneezing: metres of noise in a heist.</summary>
    public int NoiseM { get; init; }
    /// <summary>Care shortens the course by this many days.</summary>
    public int CareShortensDays { get; init; } = 1;
}

/// <summary>
/// The numbers of everyday illness, the great plague and the scarcity terms (disease-and-plague 3, 4; <c>disease/plague.jsonc</c>). Percent and permille as named; the start chance and the case
/// fatality's slider live in <c>Difficulty</c> and scale the values here.
/// </summary>
[ContentType("disease_settings")]
public sealed record DiseaseSettingsDef : ContentDef
{
    // ---- everyday onset, permille a day (4.1)
    public int CoughExposedPm { get; init; } = 20;
    public int CoughWinterPm { get; init; } = 3;
    public int HousemateAddPm { get; init; } = 50;
    public int AguePm { get; init; } = 5;
    public int FluxUnboiledPm { get; init; } = 30;
    public int FluxArmyCampPm { get; init; } = 5;
    public int ArmyCampMin { get; init; } = 50;
    public int ExposedHours { get; init; } = 4;
    /// <summary>A cough that ends in a starving or old person turns to lung fever with this chance, percent.</summary>
    public int LungFeverPct { get; init; } = 30;
    public int OldAge { get; init; } = 60;
    public int StarvingCd { get; init; } = 2500;
    /// <summary>A physician's daily visit, mites; the plague doubles it.</summary>
    public int PhysicianVisitMites { get; init; } = 100;
    public int PlagueFeeMultPct { get; init; } = 200;
    /// <summary>A physician in the settlement lowers a sick household's outbreak death multiplier by this percent of its excess over 1.</summary>
    public int AbstractPhysicOutbreakCutPct { get; init; } = 25;
    // ---- the great plague (4.2, 4.3)
    public int AttackTownPct { get; init; } = 50;
    public int AttackVillagePct { get; init; } = 35;
    public int AttackHamletPct { get; init; } = 30;
    public int FatalityPct { get; init; } = 60;
    public int EpidemicMinDays { get; init; } = 30;
    public int EpidemicMaxDays { get; init; } = 60;
    public int IncubationDays { get; init; } = 3;
    public int IllnessDays { get; init; } = 7;
    public int DeathDayMin { get; init; } = 4;
    public int DeathDayMax { get; init; } = 8;
    public int CrowdingMultPct { get; init; } = 150;
    public int BeddingBurnedPct { get; init; } = 50;
    public int ContinentSpreadPct { get; init; } = 40;
    public int ContinentSeasonsMin { get; init; } = 1;
    public int ContinentSeasonsMax { get; init; } = 2;
    public int CarrierPct { get; init; } = 30;
    public int RegionSpreadPermille { get; init; } = 30;
    public int RoadKm { get; init; } = 12;
    public int QuarantineMultPct { get; init; } = 20;
    public int QuarantineDays { get; init; } = 30;
    public int QuarantineLordPct { get; init; } = 20;
    public int HamletPopMax { get; init; } = 60;
    public int HamletDeadPct { get; init; } = 40;
    public int HamletAbandonPct { get; init; } = 25;
    // ---- the economy answers (4.5)
    public int WageMaxPm { get; init; } = 2000;
    public int WageSlopePm { get; init; } = 1500;
    public int LandMinPm { get; init; } = 400;
    public int LandSlopePm { get; init; } = 1500;
    public int RecoverPct { get; init; } = 10;
    /// <summary>A fall from the peak under this many permille moves no price (ordinary comings and goings).</summary>
    public int DeadZonePm { get; init; } = 50;
    public int PeakDecayPct { get; init; } = 5;
}
