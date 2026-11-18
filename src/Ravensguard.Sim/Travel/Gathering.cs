using Ravensguard.Content;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.Production;
using Ravensguard.Sim.Weather;
using Ravensguard.Sim.WorldGen;
using RegionData = Ravensguard.Sim.WorldGen.Region.Region;

namespace Ravensguard.Sim.Travel;

/// <summary>What one foraging sweep gave.</summary>
public sealed record SweepResult(long Grams, long TileLeftG, (string Item, string Good, long Grams, bool Risky)[] Items, int SkillUse);

/// <summary>A game animal killed: a carcass of a species with its mass, waiting to be butchered. The tile's game stock has already paid for it.</summary>
public sealed class Carcass
{
    public EntityId Id;
    public string Species = "";
    public long MassG, DeathSec;
    public int Tile;
    public bool Hung;
    public long XMm, YMm;
}

/// <summary>An unattended snare (spec 4.5): set, may catch in an hour, holds the catch until its deadline and grace pass.</summary>
public sealed class Snare
{
    public int Id, Tile;
    public long SetSec, LastHour;
    public bool Caught;
    public long CatchSec;
    public string Species = "";
    public long MassG;
    /// <summary>Where it was set (region millimetres); 0, 0 in a save from before snares had a place (the view then puts it at its tile's centre).</summary>
    public long XMm, YMm;
}

public sealed partial class TravelState
{
    public readonly List<Carcass> Carcasses = [];
    public readonly List<Snare> Snares = [];
    public ulong NextCarcass; public int NextSnare;
    public const long MicroPerG = 25, MicroDiv = 2;      // 1,000,000 micro-animals per 80 kg deer-equivalent: micro = grams x 25 / 2

    // ---------------------------------------------------------------- foraging (spec 4.4)

    public static bool ForageWindowOpen(ForageItemDef d, Season s) => d.Seasons.Count == 0 || d.Seasons.Contains(s.ToString());

    /// <summary>Grams a sweep takes: <c>min(stock x 8%, 250 x (50 + Forestry) / 100)</c>, floor; winter is x0.3 (roots only).</summary>
    public long SweepGrams(long tileStockG, int forestry, Season season)
    {
        long g = Math.Min(tileStockG * Cfg.ForagePctOfStock / 100, (long)Cfg.ForageCapG * (50 + forestry) / 100);
        if (season == Season.Winter) g = g * Cfg.ForageWinterPm / 1000;
        return Math.Max(0, g);
    }

    /// <summary>
    /// One 10-minute sweep of a tile: the grams come off the tile's forage stock and into the pack as the season's items (conservation: the same grams). Skill use d = 50. Items split by share; the first item gets the remainder.
    /// </summary>
    public SweepResult Sweep(ResourceLayer layer, int tile, int forestry, Season season, int day, Store pack)
    {
        int row = layer.Find(tile, ResourceKind.Forage);
        if (row < 0 || Goods == null) return new(0, 0, [], 0);
        var items = Content.All<ForageItemDef>().OrderBy(d => d.Id, StringComparer.Ordinal).Where(d => ForageWindowOpen(d, season)).ToList();
        if (items.Count == 0) return new(0, layer.StockG(row), [], 0);
        long want = SweepGrams(layer.StockG(row), forestry, season);
        long took = layer.Take(row, want, day);
        int sum = items.Sum(d => d.SharePm); var parts = new (string, string, long, bool)[items.Count]; long placed = 0;
        for (int i = 0; i < items.Count; i++)
        {
            long gi = i == items.Count - 1 ? took - placed : took * items[i].SharePm / sum;
            long made = 0;
            if (gi > 0) { ushort good = Goods.IdOf(items[i].Good); made = Math.Min(gi, Math.Max(0, pack.Room(good))); if (made > 0) pack.Create(good, made, CreateReason.Harvest); }
            if (made < gi) layer.Add(row, gi - made);            // no room in the pack: it stays on the tile
            parts[i] = (items[i].Id, items[i].Good, made, items[i].Risky); placed += gi;
        }
        long total = parts.Sum(p => p.Item3);
        return new(total, layer.StockG(row), parts, Cfg.ForageGainD);
    }

    /// <summary>Identification chance in permille: 300 + 7 x Forestry, at most 1000.</summary>
    public int IdentifyPm(int forestry) => Math.Min(1000, Cfg.ForageIdBasePm + Cfg.ForageIdPerSkillPm * forestry);
    /// <summary>Poisoning chance per 100 g of an unidentified risky item, in permille: (1000 - id) / 10.</summary>
    public int PoisonPmPer100g(int forestry) => (1000 - IdentifyPm(forestry)) / 10;

    /// <summary>Whether eating <paramref name="grams"/> of an unidentified mushroom poisons (stream 0x342 keyed on the person and the window of 200 game seconds).</summary>
    public bool Poisoned(EntityId person, int forestry, long grams, long sec, int item = 0)
    {
        long pm = (long)PoisonPmPer100g(forestry) * grams / 100;
        return DetRng.Range(Seed, sec / 200, person.Value, RngStreams.ForageDraw, (uint)item, 0, 1000) < pm;
    }

    // ---------------------------------------------------------------- hunting (spec 4.5)

    /// <summary>Game animals available around a tile: the whole animals in the (2r+1) square of tiles (micro-animals / 1e6), capped by the per-km2 limit.</summary>
    public int AnimalsNear(ResourceLayer layer, int tile, int radiusTiles = 6)
    {
        long sum = 0; int tx = tile % RegionData.Size, ty = tile / RegionData.Size;
        for (int dy = -radiusTiles; dy <= radiusTiles; dy++)
            for (int dx = -radiusTiles; dx <= radiusTiles; dx++)
            {
                int x = tx + dx, y = ty + dy; if (!RegionData.InBounds(x, y)) continue;
                int r = layer.Find(RegionData.Index(x, y), ResourceKind.Game); if (r >= 0) sum += layer.StockG(r);
            }
        double km2 = (2 * radiusTiles + 1) * (2 * radiusTiles + 1) * (RegionData.TileM / 1000.0) * (RegionData.TileM / 1000.0);
        return (int)Math.Min(sum / 1_000_000, Math.Floor(km2 * Cfg.GameMaxPerKm2));
    }

    /// <summary>The species a tile offers in a biome and season, by weight (stream 0x343 keyed on the tile and game hour). Null when none lives there.</summary>
    public GameSpeciesDef? PickSpecies(Biome biome, Season season, int tile, long hour, uint draw = 0)
    {
        var l = Content.All<GameSpeciesDef>().OrderBy(d => d.Id, StringComparer.Ordinal).Where(d => d.WeightPm > 0 && d.Biomes.Contains(biome.ToString()) && (d.Seasons.Count == 0 || d.Seasons.Contains(season.ToString()))).ToList();
        if (l.Count == 0) return null;
        int total = l.Sum(d => d.WeightPm), pick = DetRng.Range(Seed, hour, (ulong)tile, RngStreams.HuntDraw, draw, 0, total);
        foreach (var d in l) { if (pick < d.WeightPm) return d; pick -= d.WeightPm; }
        return l[^1];
    }

    /// <summary>
    /// A kill: the carcass's mass is taken off the game stock of the tiles around (the nearest first, one deer-equivalent = 1,000,000 micro-animals per 80 kg), a <see cref="Carcass"/> appears and mass is
    /// accounted at butchering. Mass varies 90..110% of the species mass (stream 0x343). Returns null when the land holds less than that.
    /// </summary>
    public Carcass? Kill(ResourceLayer layer, int tile, GameSpeciesDef sp, long sec, long xMm, long yMm)
    {
        long massG = sp.MassKg * 1000L * (900 + DetRng.Range(Seed, sec / TimeConst.SecondsPerHour, (ulong)tile, RngStreams.HuntDraw, 7, 0, 201)) / 1000;
        long need = massG * MicroPerG / MicroDiv, left = need; int day = (int)(sec / TimeConst.SecondsPerDay);
        int tx = tile % RegionData.Size, ty = tile / RegionData.Size;
        var cand = new List<(int D, int Row)>();
        for (int r = 0; r <= 6; r++)
            for (int dy = -r; dy <= r; dy++)
                for (int dx = -r; dx <= r; dx++)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != r) continue;
                    int x = tx + dx, y = ty + dy; if (!RegionData.InBounds(x, y)) continue;
                    int row = layer.Find(RegionData.Index(x, y), ResourceKind.Game); if (row >= 0 && layer.StockG(row) > 0) cand.Add((r, row));
                }
        if (cand.Sum(c => layer.StockG(c.Row)) < need) return null;
        foreach (var (_, row) in cand) { if (left <= 0) break; left -= layer.Take(row, left, day); }
        var car = new Carcass { Id = EntityId.MakeIn(IdKind.Other, 252, 0x2000 + ++NextCarcass), Species = sp.Id, MassG = massG, DeathSec = sec, Tile = tile, XMm = xMm, YMm = yMm };
        Carcasses.Add(car); return car;
    }

    /// <summary>The awareness distance of a game animal in metres: 30, x0.5 sneaking, x1.5 running, x0.6 downwind, x0.7 in fog or rain.</summary>
    public int AwarenessM(bool sneaking, bool running, bool downwind, bool badWeather)
    {
        long m = Cfg.AwarenessM;
        if (sneaking) m = m * Cfg.SneakPm / 1000; else if (running) m = m * Cfg.RunPm / 1000;
        if (downwind) m = m * Cfg.DownwindPm / 1000;
        if (badWeather) m = m * Cfg.BadWeatherPm / 1000;
        return (int)m;
    }

    /// <summary>
    /// A day-to-day hunt (Phase 3 cleanup B, item 2): an hour's stalking on a tile. Needs game within six tiles; succeeds with <c>250 + 5 x skill</c> permille, plus 250 with a weapon in hand, at most 900 (stream 0x343 keyed
    /// on the tile and the game hour); a success is a <see cref="Kill"/> of the species the biome and season give, its mass off the land's game stock. Returns the carcass and why (<c>ok</c>, <c>nogame</c>, <c>missed</c>).
    /// </summary>
    public (Carcass? Carcass, string Why) Hunt(ResourceLayer layer, int tile, Biome biome, Season season, long sec, long xMm, long yMm, int skill, bool armed)
    {
        if (AnimalsNear(layer, tile) <= 0) return (null, "nogame");
        long hour = sec / TimeConst.SecondsPerHour;
        var sp = PickSpecies(biome, season, tile, hour, 1); if (sp == null) return (null, "nogame");
        int p = Math.Min(900, 250 + 5 * skill + (armed ? 250 : 0));
        if (DetRng.Range(Seed, hour, (ulong)tile, RngStreams.HuntDraw, 50, 0, 1000) >= p) return (null, "missed");
        var car = Kill(layer, tile, sp, sec, xMm, yMm);
        return car == null ? (null, "nogame") : (car, "ok");
    }

    // ---------------------------------------------------------------- snares

    public Snare? SetSnare(int tile, long sec, long xMm = 0, long yMm = 0)
    {
        if (Snares.Count >= Cfg.SnareMax) return null;
        var s = new Snare { Id = ++NextSnare, Tile = tile, SetSec = sec, LastHour = sec / TimeConst.SecondsPerHour, XMm = xMm, YMm = yMm };
        Snares.Add(s); return s;
    }

    /// <summary>Per game hour a set snare may catch a hare-size animal: <c>15 + 3 x skill / 10</c> permille (stream 0x343 keyed on the tile and hour); the tile's game stock pays for it.</summary>
    public void StepSnares(ResourceLayer layer, long sec, int skill)
    {
        long hour = sec / TimeConst.SecondsPerHour;
        var hare = Content.All<GameSpeciesDef>().FirstOrDefault(d => d.Id == "hare");
        foreach (var s in Snares)
        {
            if (s.Caught || hare == null) { s.LastHour = hour; continue; }
            for (long h = s.LastHour + 1; h <= hour; h++)
            {
                if (h * TimeConst.SecondsPerHour > s.SetSec + (Cfg.SnareDeadlineHours + Cfg.SnareGraceHours) * TimeConst.SecondsPerHour) break;
                int p = Cfg.SnareBasePm + Cfg.SnareSkillPm * skill / 10;
                if (DetRng.Range(Seed, h, (ulong)s.Tile, RngStreams.HuntDraw, (uint)(100 + s.Id), 0, 1000) >= p) continue;
                long need = hare.MassKg * 1000L * MicroPerG / MicroDiv; int row = layer.Find(s.Tile, ResourceKind.Game);
                if (row < 0 || layer.StockG(row) < need) continue;
                layer.Take(row, need, (int)(h / 24)); s.Caught = true; s.CatchSec = h * TimeConst.SecondsPerHour; s.Species = hare.Id; s.MassG = hare.MassKg * 1000L; break;
            }
            s.LastHour = hour;
        }
    }

    /// <summary>Checks a snare: inside its deadline plus grace the catch is yours (a carcass); later it has spoiled and the snare is empty. The snare is taken up either way.</summary>
    public Carcass? CollectSnare(Snare s, long sec)
    {
        Snares.Remove(s);
        if (!s.Caught) return null;
        if (sec > s.SetSec + (Cfg.SnareDeadlineHours + Cfg.SnareGraceHours) * TimeConst.SecondsPerHour) return null;
        var car = new Carcass { Id = EntityId.MakeIn(IdKind.Other, 252, 0x2000 + ++NextCarcass), Species = s.Species, MassG = s.MassG, DeathSec = s.CatchSec, Tile = s.Tile };
        Carcasses.Add(car); return car;
    }
}
