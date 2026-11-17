using Ravensguard.Sim.WorldGen.History;

namespace Ravensguard.Sim.WorldGen.Region;

/// <summary>Why and how well a 16 x 16 cell window suits an embark (plan §1A Stage 3 "Quick Start", §3f).</summary>
public sealed record EmbarkScore(
    int CellX, int CellY, int Total, bool Excluded, string Reason,
    int Towns, int Villages, int Ruins, int RiverCells, int BiomeCount, int LandPct, int Population, int Realms, int NearestWarKm)
{
    /// <summary>What made up <see cref="Total"/>: (part key, points), in the order they were added. Empty for excluded windows.</summary>
    public IReadOnlyList<ScorePart> Parts { get; init; } = [];
}

public readonly record struct ScorePart(string Key, int Points);

/// <summary>Scores embark windows on the continent map and picks one automatically.</summary>
public static class EmbarkSite
{
    public const int Window = 16;
    private const int Excl = int.MinValue / 2;

    /// <summary>Continental cells of active wars (target settlement and battlefields of wars still running).</summary>
    public static List<int> ActiveWarCells(WorldHistory h)
    {
        var cells = new List<int>();
        foreach (var w in h.Wars)
        {
            if (!w.Alive) continue;
            if (w.Target >= 0 && w.Target < h.Settlements.Count) cells.Add(h.Settlements[w.Target].Cell);
            foreach (var b in h.Battlefields) if (b.War == w.Index) cells.Add(b.Cell);
        }
        return cells;
    }

    public static EmbarkScore Score(Continent c, WorldHistory h, int cx, int cy)
        => ScoreCore(c, h, cx, cy, ActiveWarCells(h));

    private static EmbarkScore ScoreCore(Continent c, WorldHistory h, int cx, int cy, List<int> warCells)
    {
        if (cx < 0 || cy < 0 || cx + Window > Continent.Size || cy + Window > Continent.Size)
            return new EmbarkScore(cx, cy, Excl, true, "outside the continent", 0, 0, 0, 0, 0, 0, 0, 0, 0);
        int land = 0, riverCells = 0, fertSum = 0, savSum = 0;
        long pop = 0;
        var biomes = new int[16];
        var realms = new bool[256];
        int riverT = c.Settings.RiverFlowThreshold;
        for (int dy = 0; dy < Window; dy++)
            for (int dx = 0; dx < Window; dx++)
            {
                int i = Continent.Index(cx + dx, cy + dy);
                if (c.Ocean[i]) continue;
                land++;
                biomes[(int)c.Biomes[i]]++;
                if (c.RiverFlow[i] >= riverT) riverCells++;
                fertSum += c.Fertility[i]; savSum += c.Savagery[i];
                if (i < h.FinalPop.Length) pop += h.FinalPop[i];
                if (h.FinalOwner.Length == Continent.Cells && h.FinalOwner[i] != Continent.None) realms[h.FinalOwner[i]] = true;
            }
        int landPct = land * 100 / (Window * Window);
        int towns = 0, villages = 0, ruins = 0;
        foreach (var s in h.Settlements)
        {
            int sx = s.Cell % Continent.Size - cx, sy = s.Cell / Continent.Size - cy;
            if (sx < 0 || sx >= Window || sy < 0 || sy >= Window) continue;
            if (s.IsRuin) { ruins++; continue; }
            pop += s.Pop;
            if (s.Pop >= 500) towns++; else villages++;
        }
        int realmCount = realms.Count(b => b);
        // Distance from the window centre to the nearest active war, in km (cells are 2 km).
        int warKm = int.MaxValue;
        int mx = cx * 2 + Window, my = cy * 2 + Window; // centre in km
        foreach (int cell in warCells)
        {
            long dxk = cell % Continent.Size * 2 + 1 - mx, dyk = cell / Continent.Size * 2 + 1 - my;
            warKm = (int)Math.Min(warKm, Region.Isqrt(dxk * dxk + dyk * dyk));
        }
        int biomeCount = 0;
        for (int b = 0; b < biomes.Length; b++) if ((Biome)b is not (Biome.Sea or Biome.Lake) && biomes[b] * 100 >= land * 10 && land > 0) biomeCount++;

        string reason = "";
        if (landPct < 65) reason = "too much sea";
        else if (warKm < 10) reason = "active war within 10 km of the centre";
        else if (towns == 0 && villages == 0) reason = "no settlements";
        int total = 0;
        var parts = new List<ScorePart>(12);
        void Add(string key, int pts) { if (pts != 0) { total += pts; parts.Add(new ScorePart(key, pts)); } }
        Add("towns", towns switch { 0 => 0, 1 => 150, 2 => 250, 3 => 320, 4 => 300, _ => -100 * (towns - 4) });
        Add("rivers", riverCells >= 8 ? 150 : riverCells > 0 ? 60 : -50);
        Add("biomes", biomeCount >= 2 ? 80 * Math.Min(biomeCount, 3) : 0);
        int mount = biomes[(int)Biome.Mountain] * 100 / Math.Max(1, land);
        int bad = (biomes[(int)Biome.Desert] + biomes[(int)Biome.Tundra]) * 100 / Math.Max(1, land);
        Add("harsh_land", -(Math.Max(0, mount - 25) * 4 + Math.Max(0, bad - 15) * 4));
        if (landPct < 95) Add("sea", -(95 - landPct) * 5);
        if (warKm < 20) Add("war", -(150 + (20 - warKm) * 10));
        Add("population", (int)Math.Clamp((pop - 8000) / 200, -60, 100));
        Add("ruins", Math.Min(ruins, 12) * 6);
        Add("realms", realmCount is >= 1 and <= 3 ? 80 : realmCount == 0 ? -100 : -40);
        Add("fertility", fertSum / Math.Max(1, land) / 2);
        Add("savagery", -Math.Max(0, savSum / Math.Max(1, land) - 60));
        bool excluded = reason.Length > 0;
        return new EmbarkScore(cx, cy, excluded ? Excl : total, excluded, reason, towns, villages, ruins, riverCells, biomeCount, landPct, (int)Math.Min(pop, int.MaxValue), realmCount, warKm == int.MaxValue ? -1 : warKm) { Parts = excluded ? [] : parts };
    }

    /// <summary>Best window by <see cref="Score"/> (ties go to the lowest y then x): three or four history towns preferred (plan §1), a river, mixed biomes, clear of active wars.</summary>
    public static EmbarkScore AutoPick(Continent c, WorldHistory h)
    {
        var wars = ActiveWarCells(h);
        EmbarkScore? best = null;
        for (int cy = 0; cy + Window <= Continent.Size; cy += 2)
            for (int cx = 0; cx + Window <= Continent.Size; cx += 2)
            {
                var s = ScoreCore(c, h, cx, cy, wars);
                if (!s.Excluded && (best == null || s.Total > best.Total)) best = s;
            }
        if (best == null) throw new InvalidOperationException("No window on this continent can host an embark.");
        var centre = best;
        for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                var s = ScoreCore(c, h, centre.CellX + dx, centre.CellY + dy, wars);
                if (!s.Excluded && s.Total > best.Total) best = s;
            }
        return best;
    }
}
