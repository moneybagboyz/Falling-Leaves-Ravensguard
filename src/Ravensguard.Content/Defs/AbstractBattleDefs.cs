namespace Ravensguard.Content;

/// <summary>
/// The abstract battle's constants (abstract-battles-v1 3.3, 4.2, 4.5; <c>abstract/battle.jsonc</c>, row type <c>abstract_battle</c>). Seconds are sim-seconds (20 fight ticks
/// each), permille unless named. The defaults are the fit against the tactical scenarios (spec 4.3; the numbers are in the spec's Notes).
/// </summary>
[ContentType("abstract_battle")]
public sealed record AbstractBattleDef : ContentDef
{
    /// <summary>One step of the exchange, sim-seconds (the spec's 15 s quantises a v2 skirmish, which is decided in 10 to 50 s, too coarsely; see the Notes).</summary>
    public int StepS { get; init; } = 1;
    public int MaxSteps { get; init; } = 1200;
    /// <summary>The press's attack rate against the duel's (the table's <c>apm</c> of the matchup), percent.</summary>
    public int PressRatePct { get; init; } = 125;
    /// <summary>Blows per minute x10 of a pursuer at a router's back (the rate of a free swing).</summary>
    public int PursuitBlowsPerMinX10 { get; init; } = 380;
    /// <summary>Fraction of the smaller side in contact: line, narrow, loose.</summary>
    public int EngagementPm { get; init; } = 594;
    public int EngagementNarrowPm { get; init; } = 800;
    public int EngagementLoosePm { get; init; } = 400;
    /// <summary>Attackers per defender at most, x1000 (2.5; narrow ground 1.5).</summary>
    public int OutnumberCapPm { get; init; } = 2500;
    public int OutnumberCapNarrowPm { get; init; } = 1500;
    public int FlankBonusPm { get; init; } = 250;
    /// <summary>The landed share of a melee blow in the press against the duel the tables were fought in (blows from two sides at once are parried and blocked less), permille.</summary>
    public int PressHitPm { get; init; } = 780;
    public int RearBonusPm { get; init; } = 500;
    /// <summary>A fall is seen by the share of the side within 10 m of the average soldier where the setup placed them, x morale_k / 10 (the lines close up as they fight).</summary>
    public int MoraleK { get; init; } = 16;
    public int RoutBelow { get; init; } = 20;
    /// <summary>A side with fewer than this share of its start strength still in the line breaks at once.</summary>
    public int CollapseActivePm { get; init; } = 0;
    /// <summary>Without its leader a side under this share breaks at once.</summary>
    public int LeaderlessCollapsePm { get; init; } = 0;
    /// <summary>First-contact casualties added by a mounted charge (mounted-combat 5): extra attack actions of each charging rider in the contact step.</summary>
    public int ChargePm { get; init; } = 700;
    public int PursuitS { get; init; } = 120;
    /// <summary>A router is in reach of pursuers this long before it has fled (the live fight's 20 m and 5 s).</summary>
    public int FleeS { get; init; } = 16;
    /// <summary>Of the enemies free of the line, this share chase routers (a balanced stance chases; a defensive one does not).</summary>
    public int PursuePm { get; init; } = 329;
    /// <summary>A soldier who breaks under 10 is cornered (and asks to surrender) with this chance.</summary>
    public int CorneredPm { get; init; } = 2252;
    /// <summary>A router under 10 with enemies still in the line is caught and yields with this chance a step.</summary>
    public int RouterCorneredPm { get; init; } = 108;
    /// <summary>A router still in the press (the enemy holds its line and the lines are in contact) is caught and yields with this chance a step while his morale is under this mark.</summary>
    public int RouterYieldBelow { get; init; } = 18;
    /// <summary>A rider strikes on each pass (hit and run): attack actions per minute x10 where he can gallop, and where he cannot (mud, snow, ice, woods: he trots in and out).</summary>
    public int RiderBlowsPerMinX10 { get; init; } = 88;
    public int RiderSlowBlowsPerMinX10 { get; init; } = 80;
    /// <summary>A footman's swings at a rider who keeps moving (permille of his press rate) and their landed share (permille of the table's): on galloping ground and where the
    /// horse is bogged (it stands longer in reach, and a mounted man cannot parry a footman well).</summary>
    public int VsRiderRatePm { get; init; } = 44;
    public int VsRiderHitPm { get; init; } = 131;
    public int VsRiderSlowRatePm { get; init; } = 120;
    public int VsRiderSlowHitPm { get; init; } = 1485;
    /// <summary>The share of the flank term a soldier in the press carries while the lines are in contact (the live flank check besets most of a line).</summary>
    public int MeleeFlankedPm { get; init; } = 1168;
    /// <summary>The lines close at this speed per side, mm per sim-second (a walk; a charging side runs).</summary>
    public int AdvanceMmps { get; init; } = 2500;
    public int ChargeMmps { get; init; } = 7700;
    public int RiderMmps { get; init; } = 9000;
    /// <summary>The lines are in contact within this gap.</summary>
    public int ContactMm { get; init; } = 2000;
    /// <summary>Shots per minute: longbow 12, crossbow 6, a throw once.</summary>
    public int LongbowPerMin { get; init; } = 12;
    public int CrossbowPerMin { get; init; } = 6;
    /// <summary>Row weights of who is hit: front, middle, rear (x1000).</summary>
    public int FrontPm { get; init; } = 1500;
    public int MiddlePm { get; init; } = 1000;
    public int RearPm { get; init; } = 345;
    /// <summary>A man already struck at by others is picked as a new opponent at weight / (1 + beset x attackers), permille (the line spreads its men along the enemy's).</summary>
    /// <summary>Each attacker beyond the first on a man cuts his own attack rate (x 1000 / (1000 + beset_rate x extra)) and raises the landed share of blows on him (x (1000 + beset_hit x extra), at most three).</summary>
    public int BesetRatePm { get; init; } = 100;
    public int BesetHitPm { get; init; } = 50;
    public int BesetPm { get; init; } = 1440;
    /// <summary>Terrain and weather (4.5): mud blows x0.9, night x0.8 and rout +10, uphill defender p_hit x1.1 (applied as a miss share), rain bows x0.85, woods range -40%.</summary>
    public int MudBlowsPm { get; init; } = 900;
    public int NightBlowsPm { get; init; } = 800;
    public int NightRoutPoints { get; init; } = 10;
    public int RainBowPm { get; init; } = 850;
    public int WoodsRangePm { get; init; } = 600;
    public int UphillHitPm { get; init; } = 1100;
    /// <summary>The aftermath: first aid by the side that holds the field (90% with a medic, 40% without), then this long of bleeding before the report counts the dead.</summary>
    public int MedicPct { get; init; } = 90;
    public int NoMedicPct { get; init; } = 40;
    public int AftermathS { get; init; } = 120;
    /// <summary>The losing side's captain down, yielded or taken is carried off by his fleeing men with this chance (0x4A3), alive and hurt.</summary>
    public int LeaderCarriedPm { get; init; } = 800;
    /// <summary>Engagement jitter +-, permille (stream 0x4A0).</summary>
    public int JitterPm { get; init; } = 50;

    // ---- Phase 7 (abstract-war-and-calibration 4.1, 4.2): v2 geometry and passes. Version 1 is the Phase 4 resolver, untouched; the "v2" row (abstract/battle_v2.jsonc) is version 2. ----
    /// <summary>1: Phase 4's press with no geometry. 2: blocks, fronts and the flank wrap, reserves, block morale, passes with trample and fall, prisoners and loot rows.</summary>
    public int Version { get; init; } = 1;
    /// <summary>A horse block this far (mm) behind its side's foot is a reserve until a foot block is under <see cref="ReservePm"/> of its start or the enemy wraps a flank.</summary>
    public int ReserveDepthMm { get; init; } = 10_000;
    public int ReservePm { get; init; } = 600;
    /// <summary>A block under <see cref="BrokenPm"/> cohesion loses its shape bonus and its members carry <see cref="BrokenX100"/> morale points off; a shield wall (foot, <see cref="WallShieldPm"/> shielded, cohesive) adds <see cref="WallX100"/>.</summary>
    public int BrokenPm { get; init; } = 600;
    public int BrokenX100 { get; init; } = 500;
    public int WallShieldPm { get; init; } = 700;
    public int WallX100 { get; init; } = 500;
    /// <summary>The pass (4.2): the men of the struck front within the refusal arc of the horse's path, the horse's training, the braced strike and the horse brought down, the trample (hit chance after the dodge, class mix), the fall's class mix.</summary>
    public int PassArcMen { get; init; } = 5;
    public int PassTrainedPm { get; init; } = 800;
    public int BraceStrikePm { get; init; } = 600;
    public int BraceDownPm { get; init; } = 450;
    public int TramplePm { get; init; } = 450;
    public int TrampleLightPm { get; init; } = 450;
    public int TrampleSeriousPm { get; init; } = 300;
    public int TrampleGravePm { get; init; } = 120;
    public int FallLightPm { get; init; } = 450;
    public int FallSeriousPm { get; init; } = 300;
    public int FallGravePm { get; init; } = 100;
    /// <summary>A rider who passes rides through (and re-forms in <see cref="ReformS"/>) or sticks in the melee with this chance.</summary>
    public int StickPm { get; init; } = 300;
    public int ReformS { get; init; } = 15;
    /// <summary>Shock (cavalry 4.1): -15 on the front of the struck block when 10+ riders pass and under <see cref="ShockBracedMaxPm"/> of that front is braced.</summary>
    public int ShockX100 { get; init; } = 1500;
    public int ShockMinRiders { get; init; } = 10;
    public int ShockBracedMaxPm { get; init; } = 250;
    /// <summary>A taken man of this skill or better, or in mail or better, is a veteran; a captain is an officer.</summary>
    public int VeteranSkill { get; init; } = 55;
}

/// <summary>The generated wound tables' header (<c>abstract/wound_tables.json</c>): the content hash they were built from, blows per cell, the builder's version.</summary>
[ContentType("wound_table_meta")]
public sealed record WoundTableMetaDef : ContentDef
{
    public string ContentHash { get; init; } = "";
    public int BlowsPerCell { get; init; }
    public int Version { get; init; }
}

/// <summary>
/// The cells of one (weapon class, armour class) of the wound tables (3.2): 7 bands (skill delta -60..+60 in steps of 20; a range band for shooting) x shield. Each cell is one
/// row of integers: <c>band, shield (0/1), n (attack actions counted), apm_x10 (the attacker's attack actions per minute in that matchup), outcome x 7 (miss, nothing, light, serious,
/// grave, severed, instant: permille of attack actions, summing to 1000), mean_pain_light, mean_pain_serious, mean_bleed_light_x10, mean_bleed_serious_x10, limb_mix x 6 (arm, leg,
/// torso, head, neck, hand: permille), fx_mix x 4 (fracture, nerve, organ, artery: permille of grave wounds)</c>.
/// </summary>
[ContentType("wound_table")]
public sealed record WoundTableDef : ContentDef
{
    public const int CellFields = 25;
    public string Weapon { get; init; } = "";
    public string Armour { get; init; } = "";
    public List<List<int>> Cells { get; init; } = [];
}

/// <summary>Recorded real wounds of one (weapon class, armour class) by outcome class (<c>abstract/wound_templates.json</c>): each template is one blow's wound records, each record
/// <c>[part, tissue, structure, organ, kind, depth_pm, grade, count, residual, bleed_x10, flags]</c>, plus the momentum that reached the skull (milli) last.</summary>
[ContentType("wound_templates")]
public sealed record WoundTemplatesDef : ContentDef
{
    public string Weapon { get; init; } = "";
    public string Armour { get; init; } = "";
    /// <summary>By outcome class name (light, serious, grave, severed, instant): the templates.</summary>
    public Dictionary<string, List<List<long>>> ByClass { get; init; } = [];
}
