using Ravensguard.Sim.Core;
using Ravensguard.Sim.Weather;

namespace Ravensguard.Sim.Travel;

/// <summary>The class of ground the hero walks on, best first (spec 4.1: highway and paved, road, track, off-road).</summary>
public enum RoadTier : byte { Highway = 0, Road = 1, Track = 2, OffRoad = 3 }

/// <summary>Terrain near the hero as bits (encounter conditions and speed).</summary>
[Flags]
public enum TerrainFlags : ushort
{
    None = 0, Woodland = 1, DeepWoodland = 2, Pasture = 4, RiverCrossing = 8, GateOrBridge = 16, Marsh = 32, LordsLand = 64, Field = 128, Mud = 256,
}

/// <summary>What the world looks like at one point on the path at one game second: everything the director, the speed model and the gate read. Built by the game from the tiles and the weather; tests use <see cref="FlatWorld"/>.</summary>
public struct LegSample
{
    public RoadTier Tier;
    public TerrainFlags Terrain;
    /// <summary>Region danger 0..1000 (cell recomputed daily, spec 4.1).</summary>
    public int DangerPm;
    public int NearestSettlementM;
    public int NearestTavernM;
    public bool Night, Evening, Daytime;
    public WeatherState Weather;
    public Season Season;
    public int ForageStockG;
    /// <summary>Game days since the last rain and storm here (int.MaxValue: none lately).</summary>
    public int RainDaysAgo, StormDaysAgo;
    public int HolyDayInDays;
    public int WarKm;
    public bool HungryYear;
    /// <summary>Game days since the last chronicle entry of a kind near here: famine, raid, war, battle.</summary>
    public int FamineDaysAgo, RaidDaysAgo, WarDaysAgo, BattleDaysAgo;
    public int SlopePm;
    /// <summary>A named ground id for the speed table: paved, road, track, field, woodland, marsh, sand, ford.</summary>
    public string Ground;
}

/// <summary>What the hero and the party are like now (interrupt inputs and the speed gate), read from the live tier at each poll.</summary>
public struct TravelStatus
{
    /// <summary>The hero's own speed on good ground in mm per game second (jog 150, trot 250; the mount, cart or party column sets it).</summary>
    public int BaseMmPerGs;
    public int HoursSinceMeal;
    public int FatiguePm;
    public int ColdStressPm, HeatStressPm;
    public bool CoreBelow36;
    /// <summary>Nearest hostile or unknown armed person in view, metres (int.MaxValue: none), and what it is.</summary>
    public int HostileM;
    public string HostileName;
    public bool PartyThreat, InCombatStance, Attacked;
    public int PartyStrengthPm;
    public bool HeroArmed;
    public bool HorseTired, HorseLame, CartStuck;
    public bool CompanionTrouble;
    public bool FoodLow;
    public int FoundWithinM;
    public bool EscortedByMentor;
    public static TravelStatus Calm(int baseMmPerGs = 150) => new() { BaseMmPerGs = baseMmPerGs, HostileM = int.MaxValue, HostileName = "", FoundWithinM = int.MaxValue, PartyStrengthPm = 500 };
}

/// <summary>The world the travel code asks: a leg sample at a point and the hero's status. The game implements it from the region layers, the weather and the live tier.</summary>
public interface ITravelWorld
{
    LegSample Sample(long sec, long xMm, long yMm);
    TravelStatus Status(long sec);
}

/// <summary>A uniform world for tests and dev tools: the same sample everywhere (a function of the game second for night and season), the status you set.</summary>
public sealed class FlatWorld : ITravelWorld
{
    public LegSample Template = Default();
    public TravelStatus Hero = TravelStatus.Calm();
    public Func<long, LegSample, LegSample>? PerSecond;
    public Func<long, long, long, LegSample, LegSample>? PerPoint;
    public int LatitudeTenths = 480;

    public static LegSample Default() => new()
    {
        Tier = RoadTier.Road, Terrain = TerrainFlags.Woodland | TerrainFlags.LordsLand, DangerPm = 300, NearestSettlementM = 5000, NearestTavernM = 9000, Daytime = true,
        Weather = WeatherState.Clear, Season = Season.Summer, ForageStockG = 1600, RainDaysAgo = int.MaxValue, StormDaysAgo = int.MaxValue, HolyDayInDays = int.MaxValue, WarKm = int.MaxValue,
        FamineDaysAgo = int.MaxValue, RaidDaysAgo = int.MaxValue, WarDaysAgo = int.MaxValue, BattleDaysAgo = int.MaxValue, Ground = "road",
    };

    public LegSample Sample(long sec, long xMm, long yMm)
    {
        var s = Template;
        var t = new GameTime(sec);
        s.Season = t.Season;
        int m = t.Hour * 60 + t.Minute, sr = Daylight.SunriseMinute(LatitudeTenths, t.DayOfYear), ss = Daylight.SunsetMinute(LatitudeTenths, t.DayOfYear);
        s.Daytime = m >= sr && m < ss; s.Night = !s.Daytime; s.Evening = m >= ss - 120 && m < ss + 180;
        if (PerSecond != null) s = PerSecond(sec, s);
        if (PerPoint != null) s = PerPoint(sec, xMm, yMm, s);
        return s;
    }

    public TravelStatus Status(long sec) => Hero;
}
