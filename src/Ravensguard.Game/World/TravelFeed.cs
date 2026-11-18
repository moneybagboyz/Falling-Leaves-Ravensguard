using System;
using Ravensguard.Content;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Production;
using Ravensguard.Sim.Travel;
using Ravensguard.Sim.Weather;
using Ravensguard.Sim.WorldGen;
using Ravensguard.Sim.WorldGen.Region;
using RegionData = Ravensguard.Sim.WorldGen.Region.Region;

namespace Ravensguard.Game.World;

/// <summary>
/// The travel code's view of the game world (<see cref="ITravelWorld"/>): the region tile under a point (road class, woodland, pasture, ford, mud), the weather the feed gives, the danger of the area, the
/// forage stock of the tile and the hero's status. Everything is read, nothing is changed; the street view fills <see cref="Hero"/> each tick.
/// </summary>
public sealed class TravelFeed(RegionData region, WeatherFeed weather, TravelSettingsDef cfg, Func<ResourceLayer?> resources, int latitudeTenths) : ITravelWorld
{
    public TravelStatus Hero = TravelStatus.Calm();
    /// <summary>Vagrants per thousand residents, a stand-in until the records give it (spec 4.1).</summary>
    public int VagrantsPer1000 = 4;
    public bool RecentRaid, WarFront, FamineYear;

    public LegSample Sample(long sec, long xMm, long yMm)
    {
        var s = FlatWorld.Default();
        var t = new GameTime(sec);
        int tx = (int)Math.Clamp(xMm / 1000 / RegionData.TileM, 0, RegionData.Size - 1), ty = (int)Math.Clamp(yMm / 1000 / RegionData.TileM, 0, RegionData.Size - 1), i = RegionData.Index(tx, ty);
        var road = (RoadClass)region.Road[i];
        s.Tier = road switch { RoadClass.Highway => RoadTier.Highway, RoadClass.Road => RoadTier.Road, RoadClass.Track or RoadClass.Lane => RoadTier.Track, _ => RoadTier.OffRoad };
        var fl = TerrainFlags.LordsLand; byte w = region.Water[i];
        if (region.Forest[i] >= 30) fl |= TerrainFlags.Woodland;
        if (region.Forest[i] >= 70) fl |= TerrainFlags.DeepWoodland;
        if (region.ParcelMap[i] != 0) fl |= TerrainFlags.Field;
        if (region.Biomes[i] == Biome.Marsh) fl |= TerrainFlags.Marsh;
        if (region.Biomes[i] is Biome.Grassland or Biome.RiverValley && region.Forest[i] < 30 && region.Fertility[i] > 40) fl |= TerrainFlags.Pasture;
        if (road != RoadClass.None && (w & (WaterFlags.River | WaterFlags.Stream)) != 0) fl |= TerrainFlags.RiverCrossing;
        var wx = weather.Sample(t, (int)(xMm / 1000), (int)(yMm / 1000));
        s.Weather = wx.State;
        if (wx.Wet > 0.8f && road != RoadClass.Highway) fl |= TerrainFlags.Mud;
        s.Terrain = fl;
        s.Ground = road switch { RoadClass.Highway => "paved", RoadClass.Road => "road", RoadClass.Track or RoadClass.Lane => "track", _ => (fl & TerrainFlags.Marsh) != 0 ? "marsh" : (fl & TerrainFlags.Woodland) != 0 ? "woodland" : "field" };
        if ((fl & TerrainFlags.RiverCrossing) != 0 && s.Tier >= RoadTier.Track) s.Ground = "ford";
        s.SlopePm = region.SlopePermille[i];
        s.Season = t.Season;
        int m = t.Hour * 60 + t.Minute, sr = Daylight.SunriseMinute(latitudeTenths, t.DayOfYear), ss = Daylight.SunsetMinute(latitudeTenths, t.DayOfYear);
        s.Daytime = m >= sr && m < ss; s.Night = !s.Daytime; s.Evening = m >= ss - 120 && m < ss + 180;
        s.DangerPm = RegionDanger.Compute(cfg, VagrantsPer1000, RecentRaid, WarFront, FamineYear, s.Night);
        s.NearestSettlementM = NearestSettlementM(xMm / 1000, yMm / 1000);
        s.NearestTavernM = s.NearestSettlementM;
        s.HolyDayInDays = (5 - t.DayOfYear % 5) % 5;
        s.RainDaysAgo = wx.State is WeatherState.LightRain or WeatherState.HeavyRain or WeatherState.Thunderstorm ? 0 : wx.Wet > 0.5f ? 1 : int.MaxValue;
        s.StormDaysAgo = wx.State == WeatherState.Thunderstorm ? 0 : int.MaxValue;
        if (resources() is { } rl && rl.Find(i, ResourceKind.Forage) is var row and >= 0) s.ForageStockG = (int)rl.StockG(row);
        return s;
    }

    int NearestSettlementM(long xM, long yM)
    {
        long best = long.MaxValue;
        foreach (var st in region.Settlements) { long dx = st.CenterX - xM, dy = st.CenterY - yM; best = Math.Min(best, dx * dx + dy * dy); }
        return best == long.MaxValue ? 99_999 : (int)Math.Min(99_999, Math.Sqrt(best));
    }

    public TravelStatus Status(long sec) => Hero;
}
