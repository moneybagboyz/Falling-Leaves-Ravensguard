namespace Ravensguard.Content;

/// <summary>Unhorsing (mounted-combat 4.3): <c>U = clamp((M_in - hold) x k, 0, max)</c> with <c>hold = hold_base + Riding x hold_per_riding</c>; a hooked attack multiplies.</summary>
public sealed record MountedUnhorseDef
{
    public int HoldBase { get; init; } = 100;
    /// <summary>Hold per point of Riding, permille of a momentum unit (1000 = 1.0).</summary>
    public int HoldPerRidingPm { get; init; } = 1000;
    /// <summary>Chance per momentum unit above the hold, basis points (0.8% = 80).</summary>
    public int KBpPerUnit { get; init; } = 80;
    public int MaxBp { get; init; } = 8000;
    public int HookPm { get; init; } = 2000;
    /// <summary>Fall: a horse falling with the rider on top pins a leg with this chance (permille), for this many ticks, and crushes it with this momentum (milli).</summary>
    public int PinPm { get; init; } = 600;
    public int PinTicks { get; init; } = 80;
    public int PinCrushMilli { get; init; } = 40_000;
    /// <summary>The fall height of a rider (mm) in <c>100 x kg x (3 x h + 0.5 x v)</c>.</summary>
    public int FallHeightMm { get; init; } = 1800;
    /// <summary>An unhorsed rider who keeps his senses is on the ground this long before he can act.</summary>
    public int DownTicks { get; init; } = 40;
    /// <summary>A refusal at a gallop throws the rider with this chance (permille).</summary>
    public int RefusalThrowPm { get; init; } = 200;
}

/// <summary>A horse's nerves (mounted-combat 4.2): panic in permille from wounds and noise, decaying; rearing and bolting thresholds.</summary>
public sealed record MountedPanicDef
{
    public int DecayPmPerS { get; init; } = 30;
    public int Cut { get; init; } = 80;
    public int Torn { get; init; } = 150;
    public int Artery { get; init; } = 300;
    public int LegBroken { get; init; } = 600;
    public int BardingStop { get; init; } = 20;
    public int FirePerS { get; init; } = 200;
    public int Thunder { get; init; } = 100;
    public int BoltsAt { get; init; } = 900;
    public int RearsAt { get; init; } = 600;
    /// <summary>Thrown from a rearing horse each second: <c>clamp(base - Riding / div, min, max)</c> percent.</summary>
    public int RearThrowBasePct { get; init; } = 40;
    public int RearThrowRidingDiv { get; init; } = 3;
    public int RearThrowMinPct { get; init; } = 5;
    public int RearThrowMaxPct { get; init; } = 40;
    public int BoltTicks { get; init; } = 200;
    /// <summary>Training at or above this damps panic to <see cref="TrainedDampPm"/>.</summary>
    public int TrainedAt { get; init; } = 900;
    public int TrainedDampPm { get; init; } = 600;
    /// <summary>Temper divisor: panic x (1 + temper / div).</summary>
    public int TemperDiv { get; init; } = 200;
    /// <summary>The cavalry AI pulls back at this panic or this much blood lost (permille).</summary>
    public int RetreatAt { get; init; } = 500;
    public int RetreatLossPm { get; init; } = 200;
}

/// <summary>Refusal before braced polearms (mounted-combat 4.4): <c>clamp(base + per x n - trained_k x (trained - 700) - riding_k x Riding, 0, max)</c> permille.</summary>
public sealed record MountedRefusalDef
{
    public int BasePm { get; init; } = 200;
    public int PerBracedPolearmPm { get; init; } = 150;
    public int TrainedK { get; init; } = 2;
    public int TrainedRef { get; init; } = 700;
    public int RidingK { get; init; } = 2;
    public int MaxPm { get; init; } = 950;
    /// <summary>The roll is made this far (mm) from the nearest braced polearm in the horse's path; the bearers counted stand within <see cref="ArcMm"/> of it.</summary>
    public int RollAtMm { get; init; } = 8000;
    public int ArcMm { get; init; } = 6000;
    /// <summary>A refusing horse stops this far out (mm) and balks this long.</summary>
    public int StopAtMm { get; init; } = 4000;
    public int BalkTicks { get; init; } = 40;
    /// <summary>The cavalry AI does not charge a line whose estimate is at or above this (permille).</summary>
    public int AiAvoidPm { get; init; } = 400;
}

/// <summary>One footing class (mounted-combat 4.5): gallop speed multiplier, the charge rule, the fastest a horse may go, and the slip or stumble rate at a gallop.</summary>
public sealed record MountedFootingDef
{
    public int SpeedPm { get; init; } = 1000;
    public bool Charge { get; init; } = true;
    /// <summary>Top speed on this footing, mm/s (0: no cap beyond the gait).</summary>
    public int MaxMmps { get; init; }
    /// <summary>A gallop here falls with this chance per second (permille): rubble stumbles 20, ice slips 80.</summary>
    public int SlipPmPerS { get; init; }
    /// <summary>Horses may not enter (stairs, ladders, trapdoors, cave mouths).</summary>
    public bool Forbidden { get; init; }
}

/// <summary>
/// Mounted combat's tunables (docs/specs/phase4/mounted-combat.md 3.2; <c>combat/mounted.jsonc</c>). Integer units are in the names; <c>_pm</c> permille, <c>_bp</c> basis points,
/// <c>_mmps</c> millimetres per second, <c>_mmps2</c> millimetres per second per second, ticks 20 Hz fight ticks.
/// </summary>
[ContentType("mounted_tuning")]
public sealed record MountedTuningDef : ContentDef
{
    /// <summary>Momentum (milli-units) per kg x m/s of closing speed.</summary>
    public int ChargeKMilliPerKgmps { get; init; } = 10;
    /// <summary>The share of the run-up a blow takes, permille: <c>lance_couched</c>, <c>tourney_couched</c>, <c>polearm_overhand</c>, <c>one_hand</c>, <c>thrown</c>, <c>bow</c>.</summary>
    public Dictionary<string, int> ChargeWeaponPm { get; init; } = [];
    public int GallopChargeMinMmps { get; init; } = 9000;
    public int ChargeMaxMmps { get; init; } = 12500;
    /// <summary>Lance on lance: the closing speed of both riders, capped.</summary>
    public int ClosingMaxMmps { get; init; } = 20_000;
    public int TrampleK { get; init; } = 8;
    /// <summary>A trample needs at least this speed (mm/s); a dodging target is hit at this share of the chance (permille).</summary>
    public int TrampleMinMmps { get; init; } = 4000;
    public int TrampleDodgePm { get; init; } = 400;
    public int TrampleRadiusMm { get; init; } = 700;
    public int TrampleStaggerTicks { get; init; } = 20;
    public int BraceReturnPm { get; init; } = 800;
    /// <summary>A braced polearm strikes at a mount closing at least this fast (mm/s), +points to hit.</summary>
    public int BraceMinMmps { get; init; } = 4000;
    public int BracePoints { get; init; } = 10;
    /// <summary>A polearm bearer must have stood still this long to brace.</summary>
    public int BraceStillTicks { get; init; } = 20;
    /// <summary>Acceleration and braking of a horse.</summary>
    public int AccelMmps2 { get; init; } = 7500;
    public int BrakeMmps2 { get; init; } = 6000;
    /// <summary>Turning rate by gait, degrees per second.</summary>
    public int TurnGallopDegPerS { get; init; } = 60;
    public int TurnTrotDegPerS { get; init; } = 150;
    public int TurnWalkDegPerS { get; init; } = 270;
    /// <summary>Accuracy points while mounted (reduced by <c>Riding / 150</c>): a couched charge, a trot, a swing at a gallop.</summary>
    public int AimChargePoints { get; init; } = 5;
    public int AimTrotPoints { get; init; } = 10;
    public int AimGallopSwingPoints { get; init; } = 20;
    /// <summary>Reach added against foot targets for non-lance weapons, mm (the rider sits 0.7 m higher).</summary>
    public int MountedReachMm { get; init; } = 400;
    /// <summary>A lance on foot: points to hit and the wind-up multiplier (permille).</summary>
    public int LanceOnFootPoints { get; init; } = 30;
    public int LanceOnFootWindupPm { get; init; } = 1500;
    /// <summary>A couched lance needs no wind-up of the arm: the point is lowered in this many ticks (the defender's window).</summary>
    public int CouchTicks { get; init; } = 3;
    /// <summary>Lance shatter: a blow of at least this momentum (units) breaks it with this chance (permille).</summary>
    public int ShatterMinUnits { get; init; } = 150;
    public int ShatterPm { get; init; } = 600;
    /// <summary>Blows at a mounted target that name no part land on the rider with this share (permille), else on the horse; the rider's near-side lower body is hit at this share.</summary>
    public int RiderSharePm { get; init; } = 600;
    public int NearSideLowerPm { get; init; } = 750;
    /// <summary>Horse side-step (Ctrl + direction): ticks, cooldown, speed, stamina points.</summary>
    public int SideStepTicks { get; init; } = 10;
    public int SideStepCooldownTicks { get; init; } = 30;
    public int SideStepMmps { get; init; } = 2500;
    public int SideStepSp { get; init; } = 12;
    /// <summary>A winded horse cannot gallop until its stamina is back above this.</summary>
    public int WindedFreeSp { get; init; } = 1500;
    /// <summary>Slope: uphill <c>x (1 - up x slope)</c> (at least <see cref="UphillMinPm"/>), downhill <c>x (1 + down x slope)</c> (at most +<see cref="DownhillCapPm"/>), a downhill charge adds to v_c.</summary>
    public int UphillKPm { get; init; } = 3000;
    public int UphillMinPm { get; init; } = 300;
    public int DownhillKPm { get; init; } = 1500;
    public int DownhillCapPm { get; init; } = 200;
    public int DownhillChargePm { get; init; } = 100;
    /// <summary>Weather: heavy rain slows the field and raises slips; a thunderstorm doubles refusal.</summary>
    public int RainFieldPm { get; init; } = 800;
    public int RainSlipPm { get; init; } = 1500;
    public int StormRefusalPm { get; init; } = 2000;
    /// <summary>A thunder clap every this many ticks in a storm.</summary>
    public int ThunderEveryTicks { get; init; } = 600;
    /// <summary>The cavalry AI: the clear lane it wants, how far it rides past, where it re-forms.</summary>
    public int AiLaneMm { get; init; } = 30_000;
    public int AiOvershootMm { get; init; } = 15_000;
    public int AiReformMm { get; init; } = 40_000;
    public int AiFlankMm { get; init; } = 20_000;
    /// <summary>A braced polearm AI braces when a mount within this distance closes at least this fast.</summary>
    public int AiBraceWithinMm { get; init; } = 30_000;
    public MountedUnhorseDef Unhorse { get; init; } = new();
    public MountedPanicDef Panic { get; init; } = new();
    public MountedRefusalDef Refusal { get; init; } = new();
    /// <summary>Footing classes by id: road, field, rough, mud, snow, ice, woods, ford, deep_water, portal.</summary>
    public Dictionary<string, MountedFootingDef> Footing { get; init; } = [];
}
