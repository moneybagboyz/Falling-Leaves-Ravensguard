using Ravensguard.Sim.Core;
using Ravensguard.Sim.WorldGen;
using Ravensguard.Sim.WorldGen.Region;

namespace Ravensguard.Sim.Sites;

/// <summary>A mine the hero opened on a prospected deposit and walks as a site (P8-LM-3; carried in from Phase 6, holdings 3.9).</summary>
public sealed record OpenedMine(ulong Site, int TileX, int TileY, MineralType Mineral, int Tonnage, int DepthM, string Name, int Year);

/// <summary>
/// A prospected deposit opened by the hero becomes a walkable underground site: a mine site of the region with one level for each 10 m of depth (at least one, at most
/// <see cref="MaxLevels"/>), in the active state, made by the same generator as the region's own mines. The site's id comes from the tile, so a second opening of the same deposit is the
/// same site; the book keeps the openings so a load puts the site back (the region itself is rebuilt from its seed). Nothing here is drawn from a stream.
/// </summary>
public static class MineSites
{
    public const int LevelM = 10, MaxLevels = 8;

    public static int LevelsFor(int depthM) => Math.Clamp((Math.Max(0, depthM) + LevelM - 1) / LevelM, 1, MaxLevels);

    public static EntityId IdOf(int tileX, int tileY) => EntityId.Make(IdKind.Other, (1UL << 46) | ((ulong)(uint)tileY << 12) | (uint)tileX);

    /// <summary>Opens the deposit as a site (idempotent); returns the site, or null when the tile is off the region.</summary>
    public static Site? Open(SiteBook book, Region region, int tileX, int tileY, MineralType mineral, int tonnage, int depthM, string name, int year)
    {
        if ((uint)tileX >= Region.Size || (uint)tileY >= Region.Size) return null;
        var id = IdOf(tileX, tileY);
        lock (book.Sync)
        {
            if (!book.OpenedMines.Any(m => m.Site == id.Value)) book.OpenedMines.Add(new OpenedMine(id.Value, tileX, tileY, mineral, tonnage, depthM, name, year));
            return Put(region, book.OpenedMines.First(m => m.Site == id.Value));
        }
    }

    /// <summary>Puts every opened mine of the book back into a region built afresh (a load, a resume); the ones already there are left alone.</summary>
    public static void Restore(SiteBook book, Region region)
    {
        lock (book.Sync) foreach (var m in book.OpenedMines) Put(region, m);
    }

    private static Site Put(Region region, OpenedMine m)
    {
        var have = region.Sites.FirstOrDefault(s => s.Id.Value == m.Site);
        if (have != null) return have;
        var s = new Site
        {
            Id = new EntityId(m.Site), Kind = SiteKind.Mine, SubKind = m.Mineral.ToString().ToLowerInvariant(), Name = m.Name, TileX = m.TileX, TileY = m.TileY,
            X = m.TileX * Region.TileM + Region.TileM / 2, Y = m.TileY * Region.TileM + Region.TileM / 2, Mineral = m.Mineral, Tonnage = m.Tonnage, Mine = MineState.Active,
            Levels = LevelsFor(m.DepthM), Year = m.Year, HeroOpened = true,
        };
        region.Sites.Add(s);
        return s;
    }
}
