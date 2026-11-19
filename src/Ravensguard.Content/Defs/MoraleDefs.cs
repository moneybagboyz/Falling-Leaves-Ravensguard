namespace Ravensguard.Content;

/// <summary>
/// Morale in a live fight (morale-capture-succession 3.1 and 4.1, P4-D; <c>combat/morale.jsonc</c>, row type <c>morale_rules</c>). Points are morale points x100 unless the name
/// says otherwise; <c>_s</c> is sim-seconds (one fight tick is a twentieth of one), <c>_m</c> metres, <c>_pm</c> permille. The defaults are the spec's numbers.
/// </summary>
[ContentType("morale_rules")]
public sealed record MoraleRulesDef : ContentDef
{
    /// <summary>Courage when the kit gives none (x100); a kit's courage (0..100) is used as it is otherwise.</summary>
    public int BaseX100 { get; init; } = 7000;
    public int FriendDownX100 { get; init; } = 600;
    public int FriendDownRadiusM { get; init; } = 10;
    /// <summary>Most a single update's new friendly falls can cost.</summary>
    public int FriendDownCapX100 { get; init; } = 2400;
    /// <summary>Penalty decay per second (0.4 points a second: 6 points are gone in 15 s).</summary>
    public int DecayX100PerS { get; init; } = 40;
    public int FlankedX100 { get; init; } = 1500;
    public int FlankedForS { get; init; } = 5;
    /// <summary>The melee layer's flank check: an enemy this close at one's side or back flanks one (and every flank blow re-applies it).</summary>
    public int FlankCheckMm { get; init; } = 2500;
    public int HitTakenX100 { get; init; } = 200;
    public int LimbLostX100 { get; init; } = 1500;
    public int RoutingNeighbourX100PerS { get; init; } = 200;
    public int RoutingNeighbourRadiusM { get; init; } = 8;
    public int RoutingCapX100 { get; init; } = 2000;
    /// <summary>Pain term: x per pain point, where pain is the share (0..100) of the knock-out threshold (0.35).</summary>
    public int PainKPm { get; init; } = 350;
    /// <summary>Blood term: x per percent of blood lost (1.0).</summary>
    public int BloodLossKPm { get; init; } = 1000;
    public int CaptainX100 { get; init; } = 1000;
    public int CaptainRadiusM { get; init; } = 15;
    public int BannerX100 { get; init; } = 800;
    public int BannerRadiusM { get; init; } = 25;
    public int ShieldWallX100 { get; init; } = 500;
    public int WinningX100 { get; init; } = 800;
    /// <summary>Kill ratio (x1000) the winning side needs in the window (2:1).</summary>
    public int WinningRatioPm { get; init; } = 2000;
    public int WinningWindowS { get; init; } = 30;
    public int OutnumberedX100 { get; init; } = 1000;
    public int OutnumberedRatioPm { get; init; } = 2000;
    public int OutnumberedRadiusM { get; init; } = 12;
    /// <summary>Alone (a decision beyond the spec, P4-D notes): no friend still fighting within this radius while an enemy is within the outnumbered radius; a lone survivor breaks.</summary>
    public int AloneX100 { get; init; } = 2000;
    /// <summary>Each enemy beyond the first within the alone radius costs a lone fighter this much more (cap <see cref="AloneCapX100"/>).</summary>
    public int AlonePerFoeX100 { get; init; } = 1500;
    public int AloneCapX100 { get; init; } = 6000;
    /// <summary>A fight with no swing, shot or throw begun for this long is over: both sides walked away (4.3).</summary>
    public int WalkAwayS { get; init; } = 15;
    public int AloneRadiusM { get; init; } = 30;
    public int LeaderDownX100 { get; init; } = 2000;
    public int LeaderDownRadiusM { get; init; } = 30;
    public int BannerLostX100 { get; init; } = 800;
    public int DarknessSuperstitiousX100 { get; init; } = 500;
    public int WaverBelowX100 { get; init; } = 3500;
    /// <summary>How long a loss counts as recent for wavering, seconds.</summary>
    public int RecentLossS { get; init; } = 15;
    /// <summary>A wavering fighter steps back one pace with this chance per second (permille; 5%).</summary>
    public int WaverStepPmPerS { get; init; } = 50;
    public int StepBackMm { get; init; } = 750;
    public int RoutBelowX100 { get; init; } = 2000;
    public int SurrenderBelowX100 { get; init; } = 1000;
    public int RallyAboveX100 { get; init; } = 4000;
    public int RallyHoldS { get; init; } = 4;
    public int RallyCaptainRadiusM { get; init; } = 20;
    /// <summary>After a rally the routing-neighbour term is ignored for this long.</summary>
    public int RallyImmuneS { get; init; } = 10;
    public int FleeEscapeM { get; init; } = 20;
    public int FleeEscapeS { get; init; } = 5;
    /// <summary>A routing fighter drops a shield below this morale (30%), the weapon below the next (30%).</summary>
    public int DropShieldBelowX100 { get; init; } = 1500;
    public int DropWeaponBelowX100 { get; init; } = 1000;
    public int DropPm { get; init; } = 300;
    public int RoutStaminaX100PerS { get; init; } = 800;
    /// <summary>Cornered: enemies within this distance in at least two of four quadrants, or one within the flee path's distance in the flee direction.</summary>
    public int CorneredM { get; init; } = 3;
    public int FleePathBlockM { get; init; } = 6;
    /// <summary>Acceptance of a surrender by the winner's kind, permille (armies 90%, bandits always, beasts never).</summary>
    public int AcceptArmyPm { get; init; } = 900;
    public int AcceptBanditPm { get; init; } = 1000;
    public int AcceptFolkPm { get; init; } = 1000;
    /// <summary>A refused yield is a fight: no new surrender for this long.</summary>
    public int RefusedRetryS { get; init; } = 10;
    /// <summary>Animals: wolves and dogs flee at this blood loss (permille), the cave beast at the next; draugr never.</summary>
    public int WolfFleeBloodPm { get; init; } = 250;
    public int CaveBeastFleeBloodPm { get; init; } = 200;
    /// <summary>Courage traits (points x100): data, not code.</summary>
    public Dictionary<string, int> TraitX100 { get; init; } = new() { ["brave"] = 1500, ["craven"] = -2000, ["drunk"] = 1000 };
    public int VeteranX100 { get; init; } = 500;
}

/// <summary>
/// Defeat, captivity, ransom and escapes (morale-capture-succession 4.4 to 4.7; <c>combat/captivity.jsonc</c>, row type <c>captivity_rules</c>). Money in mites, time in game days or
/// hours unless the name says sim-seconds (<c>_s</c>), chances in permille.
/// </summary>
[ContentType("captivity_rules")]
public sealed record CaptivityRulesDef : ContentDef
{
    /// <summary>Who won, by the encounter group's archetype: bandits, army, beasts, draugr, guards, locals.</summary>
    public Dictionary<string, string> WinnerByArchetype { get; init; } = new()
    {
        ["bandit"] = "bandits", ["soldier"] = "army", ["guard"] = "guards", ["wolf"] = "beasts", ["bear"] = "beasts", ["cave_beast"] = "beasts", ["draugr"] = "draugr", ["mob"] = "locals",
    };
    // ---- robbery (4.4) ----
    public int PittancePm { get; init; } = 50;
    public long PittanceMinMites { get; init; } = 20;
    /// <summary>Carry capacity of a robber: 15 kg + 2.5 kg x strength / 125 (strength 1250 = 40 kg), minus what the robber carries already.</summary>
    public int CarryBaseG { get; init; } = 15000;
    public int CarryPerStrengthG { get; init; } = 2500;
    public int RobberCarriesG { get; init; } = 12000;
    /// <summary>Armour that does not fit a bandit is still taken when worth this much.</summary>
    public long ArmourKeepMites { get; init; } = 400;
    public int MercyPm { get; init; } = 500;
    public int CruelKillPm { get; init; } = 500;
    /// <summary>Beasts (4.4): each minute a feeding beast finishes a downed victim with this chance, for this many minutes, unless an ally drives it off.</summary>
    public int BeastFinishPm { get; init; } = 500;
    public int BeastFeedMinutes { get; init; } = 5;
    /// <summary>What is left on a robbed body: the under layer, boots and a day's food (shape ids and good ids).</summary>
    public List<string> LeftShapes { get; init; } = ["shirt", "braies", "boots", "shoes", "hose", "chemise", "smock"];
    public List<string> LeftGoods { get; init; } = ["bread"];
    public long FoodLeftG { get; init; } = 1000;
    // ---- capture (4.5) ----
    public long KidnapMinMites { get; init; } = 1000;
    public int BanditHoldDays { get; init; } = 10;
    public int ArmyHoldDays { get; init; } = 40;
    public int BanditReleasePm { get; init; } = 400;
    public int BanditRansomPm { get; init; } = 400;
    public int BanditGuardsMin { get; init; } = 2;
    public int BanditGuardsMax { get; init; } = 3;
    public int ArmyGuards { get; init; } = 2;
    public int ShiftHours { get; init; } = 6;
    public int ArmyCareDressPm { get; init; } = 700;
    public int CaughtPainX100 { get; init; } = 2000;
    // ---- ransom (4.6) ----
    public long RankLevy { get; init; } = 1300;
    public long RankManAtArms { get; init; } = 4000;
    public long RankKnight { get; init; } = 20000;
    public long RankBaron { get; init; } = 100000;
    public long WandererFloor { get; init; } = 1500;
    public int BanditDueDays { get; init; } = 3;
    public int ArmyDueDays { get; init; } = 6;
    public int HalveAfterDays { get; init; } = 5;
    public int QuarterAfterDays { get; init; } = 10;
    public int FriendSharePm { get; init; } = 300;
    public int KeepPm { get; init; } = 100;
    // ---- escapes (4.7) ----
    public int BondsDifficulty { get; init; } = 40;
    public int BondsWorkS { get; init; } = 120;
    public int HideoutLock { get; init; } = 30;
    public int ArmyCageLock { get; init; } = 50;
    public int LockWorkS { get; init; } = 90;
    /// <summary>An improvised pick (a nail worked loose) counts this many points against the lock.</summary>
    public int ImprovisedPickPenalty { get; init; } = 10;
    public int NoticeBasePm { get; init; } = 250;
    public int DarkNoticePm { get; init; } = 500;
    public int TransferEveryMinDays { get; init; } = 2;
    public int TransferEveryMaxDays { get; init; } = 6;
    public int TransferEscapeBasePm { get; init; } = 150;
    public int RescueDailyPerMemberPm { get; init; } = 50;
    public int LootDays { get; init; } = 10;
    /// <summary>Guards, jail and slavery are Phase 6: a guard fight ends with this fine and release.</summary>
    public long GuardFineMites { get; init; } = 200;
}

/// <summary>
/// A bandit band's style (morale-capture-succession 3.2; <c>combat/bandit_styles.jsonc</c>, row type <c>bandit_style</c>): a share of bands, what they do to a victim. One per band, by the
/// band's leader (drawn on stream 0x452 by the fight id).
/// </summary>
[ContentType("bandit_style")]
public sealed record BanditStyleDef : ContentDef
{
    public int SharePm { get; init; }
    /// <summary>Kills a wounded lone victim with this chance (cruel 500).</summary>
    public int KillWoundedLonePm { get; init; }
    /// <summary>Captures anyone whose ransom estimate is at least this (kidnapper 1,000 mites); 0: by the general rule only.</summary>
    public long CaptureMinMites { get; init; }
    public bool Kidnapper { get; init; }
}
