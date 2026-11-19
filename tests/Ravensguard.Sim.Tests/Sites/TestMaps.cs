using Ravensguard.Sim.Combat;
using Ravensguard.Sim.Combat.Melee2;
using Ravensguard.Sim.Combat.Wounds;
using Ravensguard.Sim.Sites;

namespace Ravensguard.Sim.Tests.Sites;

/// <summary>Hand-made one-level site maps for the rule tests: '#' wall, '.' floor, '<' the mouth, '~' deep water (cm per map), 'b' a bier container, ':' rubble.</summary>
public static class TestMaps
{
    public static SiteMap Level(string[] rows, int waterCm = 200, ulong id = 900)
    {
        int h = rows.Length, w = rows.Max(r => r.Length), n = w * h;
        var m = new SiteMap
        {
            SiteId = id, Seed = 1, Template = "test", ContentHash = "t", W = w, H = h, Levels = 1, Tiles = new SiteTile[n], Air = new ushort[n], WaterCm = new ushort[n], Room = new short[n],
            Arrival = new int[1], FloodCm = new int[1],
        };
        Array.Fill(m.Air, (ushort)1000); Array.Fill(m.Room, (short)-1);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                char c = x < rows[y].Length ? rows[y][x] : '#';
                int i = y * w + x;
                m.Tiles[i] = c switch { '.' => SiteTile.Floor, '<' => SiteTile.StairsUp, '~' => SiteTile.Water, 'b' => SiteTile.Container, ':' => SiteTile.Rubble, '+' => SiteTile.Door, _ => SiteTile.Wall };
                if (c == '~') m.WaterCm[i] = (ushort)waterCm;
                if (c == '<') { m.Mouth = i; m.Arrival[0] = i; }
            }
        return m;
    }

    /// <summary>A straight corridor of <paramref name="length"/> cells, three wide, the mouth at its west end.</summary>
    public static SiteMap Corridor(int length, ulong id = 901)
    {
        var rows = new string[5];
        rows[0] = new string('#', length + 2);
        rows[1] = "#" + new string('.', length) + "#";
        rows[2] = "#<" + new string('.', length - 1) + "#";
        rows[3] = "#" + new string('.', length) + "#";
        rows[4] = new string('#', length + 2);
        return Level(rows, id: id);
    }

    public static CrawlMember Member(ulong person, string name = "Aldric", int skill = 45, IReadOnlyList<WornItem>? worn = null) => new()
    {
        Person = person, Name = name, Kit = new FighterKit { Name = name, Person = person, Weapon = new WeaponInstance("arming_sword", "steel"), Skill = skill, Worn = worn ?? [new("shirt", "linen"), new("gambeson", "linen")] },
        SpareTorches = 3,
    };

    public static SiteCrawl Crawl(SiteFixture fx, SiteMap m, long sec = 8 * 3600, ulong seed = 1) => new(m, fx.S, fx.C, new WoundLedger(seed, sec), sec, seed) { HeroAi = true };

    public static void Place(CrawlMember m, SiteMap map, int x, int y, int level = 0) { m.X = x * 1000 + 500; m.Y = y * 1000 + 500; m.Level = level; m.Cell = map.Index(level, x, y); }

    public static void Place(Inhabitant n, int x, int y, int level = 0) { n.X = x * 1000 + 500; n.Y = y * 1000 + 500; n.Level = level; }
}
