namespace Ravensguard.Content;

/// <summary>
/// The real-time fight's tunables (melee-and-ranged 3 to 4.7; P4-C): every number the timeline, the defence, stamina, the AI and the missiles read, so calibration is a
/// content diff (<c>combat/calibration.jsonc</c>, row type <c>melee_tuning</c>). Units are in the names: ticks are 20 Hz fight ticks, <c>_pm</c> permille, <c>_x100</c> stamina
/// hundredths, <c>_mm</c> millimetres, <c>_mmps</c> millimetres per second, <c>_q10</c> a dot product of two unit vectors scaled by 1024.
/// </summary>
[ContentType("melee_tuning")]
public sealed record MeleeTuningDef : ContentDef
{
    // ---- timeline (4.1) ----
    public int MinWindupTicks { get; init; } = 3;
    public int MinRecoverTicks { get; init; } = 2;
    /// <summary>Hold this long past a full wind-up for a heavy swing (+6 ticks, x1.5 momentum through the resolver's style).</summary>
    public int HeavyHoldTicks { get; init; } = 6;
    public int MaxHoldTicks { get; init; } = 20;
    /// <summary>A quick swing (released inside the wind-up) needs this share of the wind-up and costs this share of the swing's stamina.</summary>
    public int QuickWindupPm { get; init; } = 600;
    public int QuickStaminaPm { get; init; } = 600;
    public int HeavyStaminaPm { get; init; } = 1500;
    /// <summary>A struck fighter's wind-up is broken: recover this long.</summary>
    public int HitInterruptTicks { get; init; } = 4;
    /// <summary>Wind-up multipliers: exhausted (stamina under the mark) and weakened by blood loss.</summary>
    public int ExhaustedWindupPm { get; init; } = 1300;
    public int WeakenedWindupPm { get; init; } = 1200;
    /// <summary>Over-encumbrance: +1% timing per kg over the carrying capacity <c>15 + 2.5 x strength / 125</c> kg.</summary>
    public int OverKgTimingPm { get; init; } = 10;

    // ---- geometry (4.1) ----
    public int ReachSlackMm { get; init; } = 400;
    public int AttackConeQ10 { get; init; } = 512;
    public int FrontArcQ10 { get; init; } = 658;
    public int RearArcQ10 { get; init; } = -724;
    public int BodyRadiusMm { get; init; } = 400;
    public int TurnDegPerS { get; init; } = 540;
    public int WalkMmps { get; init; } = 3000;
    public int SprintMmps { get; init; } = 5000;
    public int DodgeMmps { get; init; } = 4300;
    /// <summary>Walking speed while winding up, striking or recovering, and while guarding, permille.</summary>
    public int AttackMovePm { get; init; } = 300;
    public int GuardMovePm { get; init; } = 500;

    // ---- defence (4.2) ----
    public int ParryWindowTicks { get; init; } = 4;
    /// <summary>Parry success: skill x 1000 / this, permille, capped.</summary>
    public int ParrySkillDivisor { get; init; } = 120;
    public int ParryCapPm { get; init; } = 950;
    public int RiposteTicks { get; init; } = 8;
    public int RiposteWindupPm { get; init; } = 500;
    /// <summary>A parried attacker staggers for its recover plus this.</summary>
    public int ParryStaggerExtraTicks { get; init; } = 4;
    public int DodgeTicks { get; init; } = 7;
    public int DodgeCooldownTicks { get; init; } = 20;
    public int DodgeEvadePm { get; init; } = 800;
    public int HeavyArmourKg { get; init; } = 20;
    public int HeavyArmourDodgePm { get; init; } = 250;
    public int FeintRecoverTicks { get; init; } = 3;
    public int FeintEveryTicks { get; init; } = 30;
    /// <summary>A defender who began a parry against a feint has no parry for this long.</summary>
    public int FeintParryDeadTicks { get; init; } = 8;
    /// <summary>Weapon cover while guarding: base + per skill point, permille; halved on the flank.</summary>
    public int WeaponCoverBasePm { get; init; } = 300;
    public int WeaponCoverPerSkillPm { get; init; } = 2;
    public int FlankWeaponCoverPm { get; init; } = 500;
    /// <summary>Cover while recovering from one's own blow (blocks at half cover), permille of the guard's.</summary>
    public int RecoverCoverPm { get; init; } = 500;
    /// <summary>Cover while staggered (parried, overpowered), permille of the guard's: off balance, the arms still come up.</summary>
    public int StaggerCoverPm { get; init; } = 500;
    public int ShieldWallBonusPm { get; init; } = 100;
    public int ShieldCoverCapPm { get; init; } = 950;
    public int ExhaustedCoverLossPm { get; init; } = 200;
    public int EmptyCoverLossPm { get; init; } = 400;
    /// <summary>A blow above the block's capacity staggers the blocker this long.</summary>
    public int OverpoweredStaggerTicks { get; init; } = 10;
    /// <summary>Shield wall (4.4): bearers within this distance, facing within 30 degrees (dot >= this), braced (moved under 1 m in 2 s).</summary>
    public int WallSpacingMm { get; init; } = 950;
    public int WallFacingQ10 { get; init; } = 887;
    public int BraceMoveMm { get; init; } = 1000;
    public int BraceTicks { get; init; } = 40;
    /// <summary>A charge (closing at sprint) into a shield bearer shoves it this far x mass ratio, and unbraces it.</summary>
    public int ShoveMm { get; init; } = 300;
    public int ShoveUnbraceTicks { get; init; } = 10;

    // ---- stamina (4.3), x100 ----
    public int StaminaMaxX100 { get; init; } = 10000;
    public int SwingBaseX100 { get; init; } = 400;
    public int SwingPerKgX100 { get; init; } = 200;
    public int BlockBaseX100 { get; init; } = 300;
    public int DodgeX100 { get; init; } = 1200;
    public int FeintX100 { get; init; } = 200;
    public int ParryFailX100 { get; init; } = 400;
    public int StandUpX100 { get; init; } = 3000;
    public int SprintPerSX100 { get; init; } = 800;
    public int ThrowBaseX100 { get; init; } = 500;
    public int BowHoldPerSX100 { get; init; } = 100;
    public int RegenRestPerSX100 { get; init; } = 1200;
    public int RegenGuardPerSX100 { get; init; } = 500;
    public int ExhaustedBelowX100 { get; init; } = 2500;
    public int WeakenedRegenPm { get; init; } = 700;

    // ---- the combat AI (4.7) ----
    public int AiFarEveryTicks { get; init; } = 4;
    public int AiNearRadiusMm { get; init; } = 60000;
    /// <summary>Reactions to an incoming blow: parry base + skill x 1000 / divisor, capped; dodge; hold block; the rest take it.</summary>
    public int ReactParryBasePm { get; init; } = 150;
    public int ReactParrySkillDivisor { get; init; } = 130;
    public int ReactParryCapPm { get; init; } = 850;
    public int ReactDodgePm { get; init; } = 120;
    public int ReactBlockPm { get; init; } = 350;
    /// <summary>A reaction is decided this many ticks before the impact at the latest (the readable red tail), and no earlier than the parry window allows.</summary>
    public int ReactLeadTicks { get; init; } = 4;
    public int AiGuardBelowX100 { get; init; } = 2200;
    /// <summary>Chance per decision to attack a foe in recover or stagger, and per think to start anyway after the patience runs out.</summary>
    public int AttackOnOpeningPm { get; init; } = 850;
    public int PatienceMinTicks { get; init; } = 6;
    public int PatienceMaxTicks { get; init; } = 26;
    /// <summary>Feint chance against a guarding foe: skill x 1000 / this.</summary>
    public int FeintSkillDivisor { get; init; } = 300;
    /// <summary>A skilled AI aims at the weakest zone with chance skill x 1000 / this.</summary>
    public int SmartAimSkillDivisor { get; init; } = 150;
    /// <summary>Spears hold at reach minus this; a polearm inside this range steps back.</summary>
    public int PolearmHoldSlackMm { get; init; } = 300;
    public int PolearmTooCloseMm { get; init; } = 1300;
    /// <summary>A polearm (or anything with long reach) used inside this distance is clumsy: points off the hit roll.</summary>
    public int CrowdedPoints { get; init; } = 0;
    /// <summary>A short weapon closing on a longer one rushes in (sprints) while the foe recovers.</summary>
    public int CloseInSprint { get; init; } = 1;

    // ---- morale (spike rules until morale-capture-succession) ----
    public int MoraleEveryTicks { get; init; } = 10;
    public int RoutBelow { get; init; } = 20;
    public int SurrenderBelow { get; init; } = 10;
    public int RallyAbove { get; init; } = 40;
    public int FriendDownPenalty { get; init; } = 6;
    public int FlankedPenalty { get; init; } = 15;
    public int HitTakenPenalty { get; init; } = 2;
    public int LimbLossPenalty { get; init; } = 15;
    public int PainMoralePm { get; init; } = 350;

    // ---- ranged (4.5) ----
    /// <summary>Aim cone at full draw: sigma = this (millidegrees) x (1 - skill / divisor).</summary>
    public int AimSigmaMdeg { get; init; } = 3000;
    public int AimSkillDivisor { get; init; } = 120;
    public int AimEarlyPm { get; init; } = 2000;
    public int AimQuickPm { get; init; } = 3000;
    public int AimMovingPm { get; init; } = 1500;
    public int AimOneEyePm { get; init; } = 1400;
    /// <summary>Full aim needs this many ticks after the full draw; a shot inside the first ten is a quick shot.</summary>
    public int AimFullTicks { get; init; } = 20;
    public int AimQuickTicks { get; init; } = 10;
    /// <summary>Velocity lost per metre flown, millionths (1.2 per mille per metre).</summary>
    public int DragPpmPerM { get; init; } = 1200;
    public int CapsuleRadiusMm { get; init; } = 250;
    public int CapsuleHeightMm { get; init; } = 1800;
    public int AimHeightMm { get; init; } = 1250;
    public int ShooterHeightMm { get; init; } = 1500;
    public int AutoLobMinM { get; init; } = 25;
    public int LobSigmaPm { get; init; } = 1500;
    public int LobApexPm { get; init; } = 40;
    public int LobSkipPm { get; init; } = 700;
    /// <summary>Archers keep their foes between these distances.</summary>
    public int ArcherMinMm { get; init; } = 15000;
    public int ArcherMaxMm { get; init; } = 40000;
    /// <summary>Moving faster than this cancels a crossbow's spanning and widens the aim.</summary>
    public int MoveCancelMmps { get; init; } = 1500;

    // ---- thrown and improvised (4.6) ----
    public int ThrowVMmps { get; init; } = 18000;
    public int ThrowMassRefG { get; init; } = 3000;
    public int ThrowStrengthRef { get; init; } = 1250;
    public int ThrowStrengthMinPm { get; init; } = 500;
    public int ThrowStrengthMaxPm { get; init; } = 1600;
    /// <summary>Thrown momentum: mass_g x v_mmps x this / 10,000 (9.7 per m/s per kg in milli-units).</summary>
    public int ThrowMomentumK { get; init; } = 97;
    public int ThrowRangePm { get; init; } = 1800;
    public int ThrowRangeCapM { get; init; } = 30;
    public int ThrowSigmaMdeg { get; init; } = 3500;
    public int ThrowWindupTicks { get; init; } = 7;
    public int ThrowRecoverTicks { get; init; } = 10;
    public int ImprovisedVelocityPm { get; init; } = 700;
    public int ImprovisedWindupMs { get; init; } = 450;
    public int ImprovisedWindupPerKgMs { get; init; } = 100;
    public int ImprovisedHitPoints { get; init; } = -10;
    /// <summary>A fragile improvised weapon loses momentum units x this / 1000 permille of condition per blow it delivers or takes (a stool breaks after about 3).</summary>
    public int FragileLossPerUnitPm { get; init; } = 3000;
}

/// <summary>A bow or crossbow (melee-and-ranged 4.5): what it launches and how hard.</summary>
[ContentType("launcher")]
public sealed record LauncherDef : ContentDef
{
    public required string Name { get; init; }
    /// <summary>The weapon shape held (it bashes in melee).</summary>
    [ContentRef(typeof(WeaponShapeDef))] public required string Shape { get; init; }
    public int PowerPm { get; init; } = 1000;
    /// <summary>A weaker archer's power scales strength / this (crossbows: 0, the windlass does it).</summary>
    public int MinStrength { get; init; }
    /// <summary>Ticks to draw at skill 0 (scaled x(400 - skill)/400); 0 for a crossbow (spanned in the reload).</summary>
    public int DrawTicks { get; init; }
    /// <summary>Ticks to nock an arrow or span a crossbow.</summary>
    public int ReloadTicks { get; init; } = 20;
    /// <summary>Released in this many ticks after the trigger or the loose.</summary>
    public int ReleaseTicks { get; init; } = 4;
    /// <summary>arrow or bolt.</summary>
    public required string AmmoKind { get; init; }
    public int RangeEffectiveM { get; init; } = 150;
    public int RangeMaxM { get; init; } = 280;
    public int V0Mps { get; init; } = 55;
    /// <summary>Spanned and held loaded without effort (crossbow); a bow is drawn and held at a stamina cost.</summary>
    public bool Holds { get; init; }
    public int QuiverSize { get; init; } = 24;
}

/// <summary>An arrow or bolt (melee-and-ranged 4.5).</summary>
[ContentType("ammo")]
public sealed record AmmoDef : ContentDef
{
    public required string Name { get; init; }
    public required string Kind { get; init; }
    /// <summary>The head's attack type (the JSON row's "type" is the content type, so this is "attack").</summary>
    public required AttackType Attack { get; init; }
    public required double ContactArea { get; init; }
    public double PenetrationMm { get; init; }
    public int MassG { get; init; } = 45;
    [ContentRef(typeof(MaterialDef))] public string HeadMaterial { get; init; } = "iron";
    /// <summary>Breakage chance, permille, on a hit and on a miss (the rest are recovered after the fight).</summary>
    public int BreakHitPm { get; init; } = 600;
    public int BreakMissPm { get; init; } = 200;
}

/// <summary>Anything with a shape that is not a weapon (melee-and-ranged 4.6): the attack is synthesised from its volume and material.</summary>
[ContentType("improvised")]
public sealed record ImprovisedDef : ContentDef
{
    public required string Name { get; init; }
    public required double VolumeCm3 { get; init; }
    [ContentRef(typeof(MaterialDef))] public required string Material { get; init; }
    public AttackType Attack { get; init; } = AttackType.Blunt;
    /// <summary>0: derived as min(60, volume^(2/3) x 2).</summary>
    public double ContactArea { get; init; }
    public double PenetrationMm { get; init; }
    public bool Fragile { get; init; }
    /// <summary>Breaks after this many blows whatever their force (a bottle: 1); 0 = by condition.</summary>
    public int BreaksAfter { get; init; }
    public bool Ignites { get; init; }
    /// <summary>What it leaves when it breaks (shards).</summary>
    public string Leaves { get; init; } = "";
}

/// <summary>A combat stance (watch-mode 3.1 presets, used by the NPC brain now): the biases the same brain reads.</summary>
[ContentType("combat_stance")]
public sealed record CombatStanceDef : ContentDef
{
    /// <summary>aggressive, balanced or defensive.</summary>
    public string Posture { get; init; } = "balanced";
    public bool NoLethal { get; init; }
    public bool Protect { get; init; }
    public int FleeBloodLossPm { get; init; } = 250;
    public int FleePainPm { get; init; } = 700;
    public int FleeMoraleBelow { get; init; } = 25;
    public bool CalledShots { get; init; } = true;
    public bool UseRanged { get; init; } = true;
    public bool TreatAllies { get; init; } = true;
    /// <summary>nearest, weakest, threat or leader.</summary>
    public string TargetPriority { get; init; } = "nearest";
    /// <summary>Attack-start bias, permille (aggressive +300): shortens the patience.</summary>
    public int AttackBiasPm { get; init; }
    /// <summary>Reaction points (defensive parry +20, aggressive -15).</summary>
    public int ParryPoints { get; init; }
    public int ReactPoints { get; init; }
    /// <summary>Attacks only on an opening (the foe recovers or staggers, or a riposte).</summary>
    public bool OpeningsOnly { get; init; }
    /// <summary>Heavy swings when the foe recovers and stamina allows.</summary>
    public bool HeavyOnOpening { get; init; }
    // ---- watch-mode 3.1 and 4.3 (P4-F): the rest of the stance table, as data the one brain reads ----
    /// <summary>The flee triggers of 4.3 beyond blood loss (pain, morale under <see cref="FleeMoraleBelow"/>, odds under <see cref="FleeOddsBelowPm"/>); off for the NPC presets, whose morale model decides.</summary>
    public bool FleeChecks { get; init; }
    /// <summary>Flee when the estimated odds of winning fall below this (permille; 0 = never by odds).</summary>
    public int FleeOddsBelowPm { get; init; }
    /// <summary>Routers are pursued up to this distance (aggressive 20 m; the others 10 m).</summary>
    public int PursueM { get; init; } = 10;
    /// <summary>Sprint in for the first blow from this close (aggressive 6 m; 0 = the ordinary close-in).</summary>
    public int ChargeWithinM { get; init; }
    /// <summary>With no foe at hand, the shield is raised toward archers within 40 m (defensive).</summary>
    public bool ShieldToArchers { get; init; }
}

/// <summary>
/// What a person of a road or site group fights with (melee 3, overview 3 <c>FightSetup</c>): the encounter group's archetype or kit names a row. Items are written
/// "shape:material" (and "shape:material:count" for things to throw); the weapon is picked per person from <see cref="Weapon"/> and <see cref="AltWeapons"/> by the person's id.
/// </summary>
[ContentType("fight_kit")]
public sealed record FightKitDef : ContentDef
{
    public string Weapon { get; init; } = "club:oak";
    public List<string> AltWeapons { get; init; } = [];
    public string Shield { get; init; } = "";
    /// <summary>Chance (permille) the person carries the shield.</summary>
    public int ShieldPm { get; init; } = 1000;
    public List<string> Worn { get; init; } = [];
    public int SkillMin { get; init; } = 20;
    public int SkillMax { get; init; } = 40;
    public string Stance { get; init; } = "balanced";
    public int Courage { get; init; } = 60;
    public string Plan { get; init; } = "human";
    public int SizeCm3 { get; init; } = 70000;
    public int Strength { get; init; } = 1250;
    public int Toughness { get; init; } = 10;
    /// <summary>A bow instead of the weapon, permille of the group: launcher, ammunition and how many arrows.</summary>
    public int BowPm { get; init; }
    public string Launcher { get; init; } = "";
    public string Ammo { get; init; } = "";
    public int AmmoCount { get; init; } = 12;
    public List<string> Throwables { get; init; } = [];
}
