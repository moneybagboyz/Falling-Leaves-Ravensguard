using Ravensguard.Content;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Weather;

namespace Ravensguard.Sim.Production;

/// <summary>An extraction job kind resolved against the good catalogue (resources spec 2.4).</summary>
public sealed class ExtractionKind
{
    public ushort Index;
    public string Id = "";
    public ResourceKind? Resource;          // null for mines (ore bodies are tracked per site)
    public bool IsMine;
    public Skill Skill;
    public long RateGPerWorkerDay;
    public int[] SeasonPermille = [1000, 1000, 1000, 1000];
    public WeatherState[] Block = [], Half = [];
    public ushort OutGood;
    public int StockFloorPct, SeasonDays, RadiusM;
    public (ushort Good, int Permille)[] Byproducts = [];
    public bool IsHunting;
}

/// <summary>Loads and applies extraction kinds: yield per worker-hour (resources spec 3.1) and the hunting roll (3.2).</summary>
public static class Extraction
{
    public static ExtractionKind[] Load(ContentDatabase db, GoodCatalog cat)
    {
        var list = new List<ExtractionKind>();
        foreach (var d in db.All<ExtractionDef>().OrderBy(e => e.Id, StringComparer.Ordinal))
        {
            if (d.SeasonMultPermille.Count != 4) throw new InvalidDataException($"extraction '{d.Id}': season_mult_permille needs four entries");
            var k = new ExtractionKind
            {
                Index = (ushort)list.Count, Id = d.Id, RateGPerWorkerDay = d.RateGPerWorkerDay, SeasonPermille = [.. d.SeasonMultPermille],
                OutGood = cat.IdOf(d.OutputGood), StockFloorPct = d.StockFloorPct, SeasonDays = d.SeasonDays, RadiusM = d.SearchRadiusM,
                Block = [.. d.WeatherBlock.Select(Enum.Parse<WeatherState>)], Half = [.. d.WeatherHalf.Select(Enum.Parse<WeatherState>)],
                Byproducts = [.. d.Byproducts.Select(b => (cat.IdOf(b.Good), b.Permille))],
                Skill = d.Skill == "None" ? Skill.None : Enum.Parse<Skill>(d.Skill),
            };
            if (d.Resource == "Mine") k.IsMine = true;
            else k.Resource = Enum.Parse<ResourceKind>(d.Resource);
            k.IsHunting = d.Id == "hunting";
            list.Add(k);
        }
        return [.. list];
    }

    /// <summary>1000 normally, 500 in a half-rate weather, 0 when it blocks the work.</summary>
    public static int WeatherMult(ExtractionKind k, WeatherState w) => Array.IndexOf(k.Block, w) >= 0 ? 0 : Array.IndexOf(k.Half, w) >= 0 ? 500 : 1000;

    /// <summary>Season multiplier at a time; a kind with <c>season_days</c> (peat) works only the first days of its season.</summary>
    public static int SeasonMult(ExtractionKind k, GameTime t)
    {
        int m = k.SeasonPermille[(int)t.Season];
        if (m > 0 && k.SeasonDays > 0 && t.DayOfSeason > k.SeasonDays) return 0;
        return m;
    }

    public static int SkillMult(int skill) => 800 + 4 * Math.Clamp(skill, 0, 100);

    /// <summary><c>min(1000, 1000 x stock / (cap x floor%))</c>; kinds without a floor are always 1000.</summary>
    public static int StockFactor(long stock, long cap, int floorPct)
    {
        if (floorPct <= 0 || cap <= 0) return stock > 0 ? 1000 : 0;
        Int128 denom = (Int128)cap * floorPct;
        return (int)Int128.Min(1000, (Int128)1000 * stock * 100 / denom);
    }

    /// <summary>
    /// Grams one worker produces in one hour: <c>rate / workday x season x weather x skill x stock_factor</c> (resources spec 3.1), floor at each step
    /// of the integer chain (G-R1: 105,000 g/h at full stock, 69,930 at a stock factor of 666).
    /// </summary>
    public static long GramsPerWorkerHour(ExtractionKind k, GameTime t, int weatherPermille, int skill, long stock, long cap, int workdayHours)
    {
        Int128 v = k.RateGPerWorkerDay / workdayHours;
        v = v * SeasonMult(k, t) / 1000;
        v = v * weatherPermille / 1000;
        v = v * SkillMult(skill) / 1000;
        v = v * StockFactor(stock, cap, k.StockFloorPct) / 1000;
        return (long)v;
    }

    /// <summary>One hunter-day kills with probability <c>min(0.5, 3 x density)</c> (stream 0x240 keyed tile and day). Density is animals per acre in micro-animals.</summary>
    public static bool HuntKill(ulong seed, int tile, long day, uint draw, long densityMicro, ResourceSettingsDef cfg)
    {
        // parts per million: min(0.5, 3 x density) with density = micro / 1e6
        long ppm = Math.Min(cfg.HuntKillMaxPermille * 1000L, densityMicro * 3);
        uint u = DetRng.NextUInt(seed, day, (ulong)tile, RngStreams.Extraction, draw);
        return (long)u * 1_000_000 < ppm * 4_294_967_296L;
    }
}

/// <summary>
/// One ore body worked by miners (resources spec 3.5): tonnage only falls, floods and cave-ins strike each season, and the ore of a vein mineral becomes the
/// matching ore good. Tonnage is kept in grams so a day's take is exact.
/// </summary>
public sealed class MineWorks
{
    public int SiteIndex;
    public EntityId SiteId;
    public WorldGen.MineralType Mineral;
    public long RemainingG, InitialG;
    public int Levels;
    public MineStateKind State;
    public int ClosedUntilDay;

    public int MaxMiners => 4 + 4 * Levels;
    public long MinedG => InitialG - RemainingG;

    public static MineWorks FromSite(int index, WorldGen.Region.Site s) => new()
    {
        SiteIndex = index, SiteId = s.Id, Mineral = s.Mineral, RemainingG = (long)s.Tonnage * 1_000_000, InitialG = (long)s.Tonnage * 1_000_000, Levels = s.Levels,
        State = s.Mine == WorldGen.Region.MineState.Active ? MineStateKind.Active : s.Mine == WorldGen.Region.MineState.Flooded ? MineStateKind.Flooded : MineStateKind.Closed,
    };

    /// <summary>Ore good a mineral yields (iron_ore, copper_ore, tin_ore, galena, gold_ore, coal, salt; stone is not worked in Phase 2; gold is, from Phase 7).</summary>
    public static string? OreGood(WorldGen.MineralType m) => m switch
    {
        WorldGen.MineralType.Iron => "iron_ore", WorldGen.MineralType.Copper => "copper_ore", WorldGen.MineralType.Tin => "tin_ore",
        WorldGen.MineralType.Silver => "galena", WorldGen.MineralType.Gold => "gold_ore", WorldGen.MineralType.Coal => "coal", WorldGen.MineralType.Salt => "salt", _ => null,
    };

    /// <summary>Grams mined in one worker-day by <paramref name="miners"/> (and <paramref name="haulers"/>, -1 for hauling not limiting): <c>0.5 t each</c>, bad air halves it past 40% mined at 2+ levels.</summary>
    public long DayYieldG(ExtractionKind k, int miners, int haulers, int skillMult = 1000, int today = int.MaxValue, long rateOverrideG = -1)
    {
        if (today < ClosedUntilDay || State != MineStateKind.Active || miners <= 0 || RemainingG <= 0) return 0;
        int eff = Math.Min(miners, MaxMiners);
        if (haulers >= 0) eff = Math.Min(eff, 2 * haulers);
        long g = (rateOverrideG >= 0 ? rateOverrideG : k.RateGPerWorkerDay) * eff * skillMult / 1000;
        if (Levels >= 2 && InitialG > 0 && MinedG * 100 > InitialG * 40) g /= 2;
        return Math.Min(g, RemainingG);
    }

    public long Mine(long g) { long t = Math.Min(g, RemainingG); RemainingG -= t; return t; }

    /// <summary>Seasonal hazards (stream 0x241 keyed site id and the season's hour): flood <c>3% x levels</c> (x2 in a wet year, x1.5 river high), cave-in <c>0.5% x levels</c>.</summary>
    public MineHazard RollHazards(ulong seed, GameTime now, bool wetYear, bool riverHigh, ResourceSettingsDef cfg)
    {
        if (State == MineStateKind.Closed) return MineHazard.None;
        long hour = now.TotalHours;
        int floodPermille = cfg.MineFloodPctPerLevel * 10 * Levels * (wetYear ? 2 : 1);
        if (riverHigh) floodPermille = floodPermille * 3 / 2;
        uint a = DetRng.NextUInt(seed, hour, SiteId.Value, RngStreams.MineHazard, 0), b = DetRng.NextUInt(seed, hour, SiteId.Value, RngStreams.MineHazard, 1);
        bool flood = State == MineStateKind.Active && (long)a * 1000 < (long)floodPermille * 4_294_967_296L;
        bool cave = State == MineStateKind.Active && (long)b * 1000 < (long)cfg.MineCaveInPermillePerLevel * Levels * 4_294_967_296L;
        int dead = cave ? 1 + (int)(DetRng.NextUInt(seed, hour, SiteId.Value, RngStreams.MineHazard, 2) & 1) : 0;
        if (flood) State = MineStateKind.Flooded;
        if (cave) ClosedUntilDay = (int)now.TotalDays + 15;
        return new MineHazard(flood, cave, dead);
    }
}

public enum MineStateKind : byte { Active, Flooded, Closed }
public readonly record struct MineHazard(bool Flood, bool CaveIn, int Dead)
{
    public static readonly MineHazard None = new(false, false, 0);
}
