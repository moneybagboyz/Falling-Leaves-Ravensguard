namespace Ravensguard.Content;

/// <summary>
/// A breed of working or riding animal (horses-and-carts spec 3.2). Speeds are centimetres per real second (500 = the plan's 5 m/s trot), stamina is in stamina points,
/// costs per metre, regeneration per game second; every number is content. <c>trot</c> and <c>gallop</c> are 0 for a breed that has no such gait (an ox).
/// </summary>
[ContentType("species")]
public sealed record SpeciesDef : ContentDef
{
    /// <summary>Horse, Ox, Mule, Donkey or Dog.</summary>
    public required string Species { get; init; }
    public string Breed { get; init; } = "";
    public string Name { get; init; } = "";
    public int MassKg { get; init; }
    /// <summary>Rider plus gear a riding animal carries sustained.</summary>
    public int CarryKg { get; init; }
    /// <summary>Sustained pull over a working day, newtons (about 12 percent of body weight).</summary>
    public int PullN { get; init; }
    public Dictionary<string, int> GaitCmps { get; init; } = [];
    public int StaminaSp { get; init; } = 10_000;
    public Dictionary<string, int> StaminaCostSpPerM { get; init; } = [];
    /// <summary>Stamina regained per game second: <c>walk</c> while walking, <c>rest</c> while standing.</summary>
    public Dictionary<string, int> RegenSpPerGs { get; init; } = [];
    public int IdleHayKgDay { get; init; } = 7;
    public int WorkHayKgDay { get; init; } = 8;
    /// <summary>Oats a working animal eats a day, in hectograms (2.5 kg = 25).</summary>
    public int WorkOatsHgDay { get; init; }
    public int WaterLDay { get; init; } = 35;
    public int ShoeLifeKm { get; init; } = 600;
    public int PriceMites { get; init; }
    /// <summary>Body plan id (<c>horse</c> arrives with Phase 4; Phase 3 uses a quadruped stub).</summary>
    public string BodyPlan { get; init; } = "quadruped";
    /// <summary>Sprite id the view draws (the 64 x 64 animal and draught sprites).</summary>
    public string Sprite { get; init; } = "";
    /// <summary>Whether it can be ridden (a horse) and whether it works in harness.</summary>
    public bool Ridden { get; init; }
    public bool Draught { get; init; } = true;
    /// <summary>Mounted combat (Phase 4): training and temper a horse of this breed is bred with (a war horse 900 and steady 10); 0 training keeps the herd's roll.</summary>
    public int TrainedPm { get; init; }
    public int Temper { get; init; }
    /// <summary>P6-B6 (holdings 3.7): the chance in percent that a mare and a stallion kept together breed in a game year (60); 0 for a species that does not breed as individuals (an ox is a herd count, a mule is sterile).</summary>
    public int FoalPct { get; init; } = 60;
    /// <summary>P6-B6: game days from covering to foaling (11 months scaled x60/365, as human pregnancy is 45).</summary>
    public int GestationDays { get; init; } = 56;
    /// <summary>P6-B6: the age in game years at which a foal can be ridden or worked.</summary>
    public int MaturityYears { get; init; } = 3;
}

/// <summary>One part of a vehicle (spec 3.3): what it weighs, what it carries, how tough it is and what it needs below it.</summary>
[ContentType("vehicle_part")]
public sealed record VehiclePartDef : ContentDef
{
    public string Name { get; init; } = "";
    public int MassKg { get; init; }
    public int VolumeL { get; init; }
    public int CargoKg { get; init; }
    public int CargoL { get; init; }
    public int Hp { get; init; } = 100;
    [ContentRef(typeof(VehiclePartDef))] public List<string> NeedsBelow { get; init; } = [];
    public List<string> Tags { get; init; } = [];
    // ---- P5-G (vehicle-designer 3.1): all optional
    /// <summary>The designer's palette category: frame, wheels, hitch, cargo, seats, hull, steering, sail, cover, workstations.</summary>
    public string Category { get; init; } = "";
    /// <summary>The craft recipe that makes the part (a <c>vehicle_part</c> result); empty for a part nobody makes yet.</summary>
    public string Recipe { get; init; } = "";
    /// <summary>The recipe that mends it (a vehicle-part repair line); empty: replace it.</summary>
    public string Repair { get; init; } = "";
    /// <summary>Worker-hours to fit it on the frame (the one worker-hour rule applies).</summary>
    public double InstallH { get; init; }
    /// <summary>What it steers with: pole (the fore-carriage of a four-wheeler), pivot_axle, shafts, handles, rudder, steering_oar.</summary>
    public string Steer { get; init; } = "";
    /// <summary>An axle's load, kilograms (the design's axle limit is the sum, unless the template names one).</summary>
    public int AxleKg { get; init; }
    /// <summary>Hull form of a hull cell (permille of the 1 m2 cell at the waterline; block coefficient), and the hull's depth; a keel's form wins over the planks' (BoatMath).</summary>
    public int WaterplanePm { get; init; }
    public int HullDepthMm { get; init; }
    public int BlockPm { get; init; }
    public bool Buoyant { get; init; }
    /// <summary>Seats that row, and people who pole (a quant-pole rack).</summary>
    public int Rowers { get; init; }
    public int Polers { get; init; }
    public int SailM2 { get; init; }
    /// <summary>The part's centre of mass above the keel or the ground, mm.</summary>
    public int HeightMm { get; init; }
    /// <summary>Hit-point loss scale on bad ground, permille (a dished wheel takes ruts better).</summary>
    public int RoughWearPm { get; init; } = 1000;
    /// <summary>The craft station a part is (a mobile hearth is a <c>hearth</c>), or empty.</summary>
    public string Station { get; init; } = "";
}

/// <summary>P5-G: an upper deck of a vehicle design (a barge's cabin roof, a cart's tilt is on deck 0): its rows over the same legend.</summary>
public sealed record VehicleLevelDef { public int Deck { get; init; } = 1; public List<string> Rows { get; init; } = []; }

/// <summary>A hitch: how many animals pull the vehicle and by what (shafts, pole_yoke, pushed, none).</summary>
/// <summary><c>slots</c> is the animals of one team (a pair of oxen, one horse); <c>max_animals</c> is how many may be hitched in all (0 means one team): extra teams are added in front, each a tenth less efficient.</summary>
public sealed record VehicleHitchDef { public int Slots { get; init; } public int MaxAnimals { get; init; } public string Kind { get; init; } = "shafts"; }

/// <summary>
/// A fixed template vehicle as text (spec 3.3, plan 3h): the same <c>legend</c> and <c>rows</c> as a building blueprint, each legend letter a stack of parts from the ground up.
/// Phase 3 has no designer. <c>base_speed_cmps</c> is the speed on a good road with a team that is strong enough; <c>axle_limit_kg</c> is 0 for a vehicle with no axle load rule.
/// </summary>
[ContentType("vehicle_blueprint")]
public sealed record VehicleBlueprintDef : ContentDef
{
    public required string Name { get; init; }
    /// <summary>0: derived from the hitch (P5-G designs).</summary>
    public int BaseSpeedCmps { get; init; }
    public int AxleLimitKg { get; init; }
    /// <summary>0: derived from the axles (P5-G: one axle 4 m, two 8 m, x0.6 with a pivoting axle).</summary>
    public int TurningRadiusDm { get; init; }
    public VehicleHitchDef Hitch { get; init; } = new();
    public int Driver { get; init; } = 1;
    public int Passengers { get; init; }
    /// <summary>True for a vehicle that runs on runners (a sledge): snow and ice resistance differ.</summary>
    public bool Runners { get; init; }
    /// <summary>The Phase 2 caravan vehicle this template is (<c>cart</c>, <c>ox_wagon</c>, <c>handcart</c>), or empty.</summary>
    public string CaravanDef { get; init; } = "";
    public required Dictionary<string, List<string>> Legend { get; init; }
    public required List<string> Rows { get; init; }
    /// <summary>Sprite id the view draws.</summary>
    public string Sprite { get; init; } = "";
    /// <summary>P5-G: upper decks (vehicle-designer 4.1), each a grid of the same size over the same legend.</summary>
    public List<VehicleLevelDef> Levels { get; init; } = [];
    /// <summary>P5-G: where it is built (<c>yard</c> or <c>slipway</c>) and how it moves on water (<c>poled</c>, <c>rowed</c>, <c>towed</c>, <c>sail</c>, <c>rope</c>), empty on land.</summary>
    public string BuiltAt { get; init; } = "";
    public List<string> Power { get; init; } = [];
}

/// <summary>P5-G (vehicle-designer 3.4): river depth and current by width class, the water drag and the people's pull on oars and poles.</summary>
[ContentType("vehicle_water")]
public sealed record VehicleWaterDef : ContentDef
{
    /// <summary>Width classes, narrowest first: up to <c>max_width_cm</c> (0: no limit) the depth and the current.</summary>
    public List<WaterClassDef> Classes { get; init; } = [];
    public int FordDepthMm { get; init; } = 500;
    public int HighDayDepthMm { get; init; } = 300;
    public int HighDayCurrentMmps { get; init; } = 300;
    public int HighDaysCap { get; init; } = 3;
    public int DragK { get; init; } = 8;
    public int RowerN { get; init; } = 70;
    public int PolerN { get; init; } = 120;
    public int PolingMaxDepthMm { get; init; } = 3000;
    public int KeelClearanceMm { get; init; } = 150;
    public int TenderGmMm { get; init; } = 150;
    public int BeamSeaWindMps { get; init; } = 10;
    public int CapsizePm { get; init; } = 20;
    public int WaveFreeboardMm { get; init; } = 100;
    public int SwampLps { get; init; } = 20;
    public int BailLps { get; init; } = 5;
    public int FerryMmps { get; init; } = 500;
    public int FerryFeePerson { get; init; } = 15;
    public int FerryFeeCart { get; init; } = 40;
    public int FishKgPerCrewDay { get; init; } = 12;
    public int FishWinterPm { get; init; } = 400;
    public int FishShoreKgDay { get; init; } = 4;
}

public sealed record WaterClassDef { public required string Id { get; init; } public int MaxWidthCm { get; init; } public int DepthMm { get; init; } public int CurrentMmps { get; init; } }

/// <summary>Rolling resistance (permille of weight) per surface, and the sledge's own column; the live tier and the abstract caravans read the same table (spec 4.5).</summary>
[ContentType("vehicle_tuning")]
public sealed record VehicleTuningDef : ContentDef
{
    public Dictionary<string, int> CrrPm { get; init; } = [];
    public Dictionary<string, int> RunnerCrrPm { get; init; } = [];
    /// <summary>Newtons one pusher adds per point of strength.</summary>
    public int PushNPerStrength { get; init; } = 6;
    public int StuckCrrMultPm { get; init; } = 1500;
    /// <summary>Permille of a wheel's hit points worn per 20 m at full axle load.</summary>
    public int WheelWearPm { get; init; } = 1;
}
