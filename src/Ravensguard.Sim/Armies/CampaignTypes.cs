namespace Ravensguard.Sim.Armies;

/// <summary>The campaign brain's numbers (armies-on-the-map 4.10, 4.11): constants in the pattern of <see cref="MarchConfig"/>; every figure an integer.</summary>
public static class CampaignConfig
{
    /// <summary>A fief taken is worth four years of its receipts, a sack one; a claim adds 30%; a lord captured is 2,000 cp at one chance in five.</summary>
    public const int TakeYears = 4, SackYears = 1, ClaimPct = 30, LordCp = 2000, LordChancePct = 20;
    /// <summary>Metres a column marches a day (3.7 km), the reach of a campaign in march days, and the siege days of a palisade and of stone walls (plus the garrison over 100).</summary>
    public const int MarchMPerDay = 3700, ReachDays = 8, PalisadeDays = 2, StoneDays = 8, StoneLiftDays = 30;
    /// <summary>What a host of the other side is worth if it is beaten in the field: cp a known man.</summary>
    public const int HostManCp = 300;
    /// <summary>Scouts ride 10 km ahead and see 2 km (by day, 05:00 to 21:00); villagers within 3 km carry the news of a column (rumour); hosts within 2 km meet; the ratio under which a host avoids battle (permille); the hour of the battle.</summary>
    public const int ScoutM = 12_000, RumourM = 3000, ContactM = 2000, AvoidPermille = 700, BattleHour = 8;
    /// <summary>An intel row's disc is 4 km wide for each day of its age (500 m when fresh); its strength is trusted less by a tenth a day, to a floor of two fifths.</summary>
    public const int DiscMPerDay = 4000, DiscMinM = 500, AgeLossPm = 100, AgeFloorPm = 400, IntelKeepDays = 12;
    /// <summary>The abstract resolver fights at most this many men a side, in the ratio of the two hosts (the rest are the reserve that was not engaged).</summary>
    public const int AbstractCap = 120;
    /// <summary>War score from what armies do (4.11): a field battle won (the loser's lost percent / 2, to this), a place taken (castle or town, palisade), a raid, a host destroyed; and the cap on raids a season.</summary>
    public const int BattleMax = 15, TakenStone = 20, TakenPalisade = 8, RaidPoints = 2, RaidMax = 10, DestroyedPoints = 12, HarvestWeariness = 10;
}

/// <summary>What happened on the map that moves a war's score (4.11).</summary>
public enum WarEventKind : byte { FieldBattle, PlaceTaken, Relief, Raid, LordCaptured, HostDestroyed, HarvestHeld }

/// <summary>One thing an army did in a war (4.11): who did it, what it earned the doer, who bled. The season's war step reads the ones not yet scored.</summary>
public sealed class WarEvent
{
    public int War, Day, Doer, Points, Lost, OwnLost, Place;
    public WarEventKind Kind;
    public ulong Entry;
    public bool Scored;
}

/// <summary>What a realm last knew of one host of the other side (4.10): when, where (the centre of its disc; the disc widens with the age) and how many; a scout's sighting (0) or a rumour of a village (1).</summary>
public sealed class IntelRow
{
    public int Observer, Subject, Day, Strength;
    public long XMm, YMm;
    public byte Source;
}

/// <summary>A host's campaign (4.10): the objective, what the plan was worth when it was made, and where the host is in it; a battle waits for its hour.</summary>
public sealed class HostCampaign
{
    public int Host, Objective = -1, PlanDay, EnemyHost, SiegeDay = -1, Days;
    /// <summary>0 take a fief's seat, 1 sack, 2 meet the host.</summary>
    public byte Kind;
    /// <summary>0 ready (to march), 1 marching, 2 at the objective, 3 over.</summary>
    public byte Stage;
    public long Value, Cost, Score;
    public int PWinPm;
    /// <summary>The game hour a pending battle is fought (0: none).</summary>
    public long BattleHour;
}

/// <summary>A campaign candidate as the brain scored it (4.10): the numbers are in the chronicle's entry.</summary>
public sealed record CampaignScore(int Target, byte Kind, long Value, int MarchDays, int SiegeDays, int PWinPm, long Cost, long Score, int EnemyHost)
{
    public int Days => Math.Max(1, MarchDays + SiegeDays);
}

/// <summary>What the season's war step reads for one war: the events not yet scored, and the harvests each side kept a levy host in the field.</summary>
public sealed record WarScoreReport(IReadOnlyList<WarEvent> Events, int HeldAttacker, int HeldDefender);

public enum DefenceClass : byte { Open, Palisade, Stone }
public enum SiegeVerdict : byte { Continue, Taken, Lifted }

/// <summary>
/// The narrow seam the siege book (SG-D) fills: how a place is held, how many days its siege is expected to take, and what became of a siege today. The stub is what the campaign brain knows
/// without it: a town is stone, a village palisaded, a farmstead open; a stone place stands until the host gives up (no engines, no breach).
/// </summary>
public interface ISiegeHook
{
    DefenceClass DefenceOf(ArmyWorld aw, int settlement);
    int SiegeDays(ArmyWorld aw, int settlement, int garrison);
    SiegeVerdict Resolve(ArmyEnv e, ArmyWorld aw, ArmyHost host, int settlement, int daysBesieged);
}

public sealed class StubSiege : ISiegeHook
{
    public DefenceClass DefenceOf(ArmyWorld aw, int settlement) => aw.Region.Settlements[settlement].Class switch { Ravensguard.Sim.WorldGen.Region.SettlementClass.Town => DefenceClass.Stone, Ravensguard.Sim.WorldGen.Region.SettlementClass.Village => DefenceClass.Palisade, _ => DefenceClass.Open };
    public int SiegeDays(ArmyWorld aw, int settlement, int garrison) => DefenceOf(aw, settlement) switch { DefenceClass.Open => 0, DefenceClass.Palisade => CampaignConfig.PalisadeDays, _ => CampaignConfig.StoneDays + garrison / 100 };
    public SiegeVerdict Resolve(ArmyEnv e, ArmyWorld aw, ArmyHost host, int settlement, int daysBesieged) => DefenceOf(aw, settlement) switch
    {
        DefenceClass.Open => SiegeVerdict.Taken,
        DefenceClass.Palisade => daysBesieged >= CampaignConfig.PalisadeDays ? SiegeVerdict.Taken : SiegeVerdict.Continue,
        _ => daysBesieged >= CampaignConfig.StoneLiftDays ? SiegeVerdict.Lifted : SiegeVerdict.Continue,
    };
}

/// <summary>The two hosts of a battle the player's bubble may take (the hook returns null to leave it to the abstract resolver).</summary>
public sealed record FieldEngagement(ArmyHost A, ArmyHost B, int Place);

/// <summary>What a live battle left (the bubble has killed and wounded its people): the winning host's id and the battle's status commands for both hosts' men.</summary>
public sealed record FieldVerdict(int WinnerHost, IReadOnlyList<Ravensguard.Sim.Combat.Abstract.BattleCommand> Commands, ulong Entry);
