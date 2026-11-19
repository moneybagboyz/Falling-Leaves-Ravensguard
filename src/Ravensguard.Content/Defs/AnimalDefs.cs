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
}

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
    public int BaseSpeedCmps { get; init; } = 250;
    public int AxleLimitKg { get; init; }
    public int TurningRadiusDm { get; init; } = 40;
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
}

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
