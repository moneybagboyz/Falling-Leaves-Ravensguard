namespace Ravensguard.Content;

/// <summary>One alternative a treatment may consume: <c>count</c> of the good <c>item</c> (medicine spec 3).</summary>
public sealed record TreatNeedDef
{
    public required string Item { get; init; }
    public int Count { get; init; } = 1;
}

/// <summary>
/// One treatment (medicine spec 3, <c>medicine/treatments.jsonc</c>): its time in sim-seconds, the surgery skill floor, what it uses (alternatives in order; the first held is taken),
/// the tool class a surgery needs and whether it needs light. The id is the <c>TreatKind</c> name in snake case (<c>set_bone</c>).
/// </summary>
[ContentType("treatment")]
public sealed record TreatmentDef : ContentDef
{
    public required int SimS { get; init; }
    public int MinSkill { get; init; }
    public List<TreatNeedDef> Needs { get; init; } = [];
    public string ToolQuality { get; init; } = "";
    public int ToolLevel { get; init; }
    public bool Light { get; init; }
}

/// <summary>
/// The healing table of wounds 3.4 (<c>medicine/healing.jsonc</c>), days x 10, moved out of code with its values unchanged. A skin, inset or vessel wound heals in
/// the shallow or the deep time (deep from <c>deep_from_pm</c> of depth); a muscle in <c>muscle_x10</c> by grade (1, 2, 3 and over) and a ligament in <c>ligament_x10</c> (sprained, worse),
/// both times <c>untreated_x10 / 10</c> when untreated (<c>treated_x10 / 10</c> when treated); a bone by grade in the treated and untreated lists; malunion percent by bone grade.
/// </summary>
[ContentType("healing_table")]
public sealed record HealingTableDef : ContentDef
{
    public int ShallowTreatedX10 { get; init; } = 60;
    public int ShallowX10 { get; init; } = 100;
    public int DeepTreatedX10 { get; init; } = 80;
    public int DeepX10 { get; init; } = 140;
    public int DeepFromPm { get; init; } = 500;
    public List<int> MuscleX10 { get; init; } = [30, 150, 400];
    public List<int> LigamentX10 { get; init; } = [120, 300];
    public int TreatedX10 { get; init; } = 10;
    public int UntreatedX10 { get; init; } = 15;
    /// <summary>Bone healing by grade 1 to 4 when treated (set or splinted), and untreated.</summary>
    public List<int> BoneTreatedX10 { get; init; } = [100, 200, 280, 400];
    public List<int> BoneX10 { get; init; } = [150, 350, 450, 600];
    public int BoneOtherX10 { get; init; } = 30;
    public int TendonX10 { get; init; } = 400;
    public int OrganRepairedX10 { get; init; } = 300;
    public int SeveredX10 { get; init; } = 100;
    /// <summary>Malunion chance of an unset fracture by bone grade 1 to 4, percent.</summary>
    public List<int> MalunionPct { get; init; } = [0, 50, 80, 100];
}

/// <summary>Pain relief of one dose (<c>medicine/relief.jsonc</c>): points a game hour for <c>hours</c> game hours.</summary>
[ContentType("pain_relief")]
public sealed record PainReliefDef : ContentDef
{
    public required int PointsPerHour { get; init; }
    public int Hours { get; init; } = 4;
}

/// <summary>A walking or carrying aid (<c>medicine/aids.jsonc</c>): hands it takes, the walk capacity it gives in permille, and the speed of a litter.</summary>
[ContentType("medical_aid")]
public sealed record MedicalAidDef : ContentDef
{
    public int Hands { get; init; } = 1;
    /// <summary>Walk capacity with one usable leg (permille of a sound man's), 0 when the aid does not help.</summary>
    public int OneLegPm { get; init; }
    /// <summary>Walk capacity with no usable leg and two of the aid.</summary>
    public int NoLegPm { get; init; }
    public int Carriers { get; init; }
    public int CarrySpeedPm { get; init; }
}

/// <summary>A physician's service and its fee in mites (<c>medicine/prices.jsonc</c>); <c>barber</c> says whether a barber-surgeon offers it.</summary>
[ContentType("medicine_price")]
public sealed record MedicinePriceDef : ContentDef
{
    public required int Mites { get; init; }
    public bool Barber { get; init; }
}

/// <summary>The numbers of physicians and barbers (medicine 4.4): where they are, how good, how often they treat, what a barber charges.</summary>
[ContentType("medicine_rules")]
public sealed record MedicineRulesDef : ContentDef
{
    public int BarberSharePct { get; init; } = 60;
    public int BedDayMites { get; init; } = 60;
    public int PhysicianTreatPct { get; init; } = 80;
    public int BarberTreatPct { get; init; } = 60;
    /// <summary>One physician per this many people of a town (at least one).</summary>
    public int PeoplePerPhysician { get; init; } = 1000;
    /// <summary>A village of this many people or more has a barber-surgeon.</summary>
    public int BarberMinPeople { get; init; } = 150;
    public int PhysicianSurgeryBase { get; init; } = 45;
    public int PhysicianSurgerySpread { get; init; } = 26;
    public int BarberSurgeryBase { get; init; } = 25;
    public int BarberSurgerySpread { get; init; } = 16;
}
