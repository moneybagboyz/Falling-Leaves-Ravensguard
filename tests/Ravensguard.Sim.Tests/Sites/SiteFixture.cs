using System.Text;
using Ravensguard.Content;
using Ravensguard.Sim.Combat;
using Ravensguard.Sim.Combat.Melee2;
using Ravensguard.Sim.Sites;
using Ravensguard.Sim.Tests.Combat.Resolver2;
using Ravensguard.Sim.WorldGen.Region;

namespace Ravensguard.Sim.Tests.Sites;

/// <summary>Content, the site content and the fight content, loaded once for the exploration tests (P4-H).</summary>
public sealed class SiteFixture
{
    public ContentDatabase Db { get; }
    public SiteContent S { get; }
    public FightContent C { get; }

    public SiteFixture()
    {
        Db = V2Fixture.Load();
        S = new SiteContent(Db);
        C = new FightContent(new CombatTables(Db), Db);
    }

    /// <summary>The abbey of the spec's example (4.2, EX-4, EX-12): 120 at its peak, sacked 60 years ago, three levels.</summary>
    public static SiteHistoryInfo Abbey(ulong id = 4401, int levels = 3, int years = 60) => new()
    {
        SiteId = id, Kind = SiteKind.Ruin, Sub = "sacked", Name = "Ruins of Aldwick Abbey", Levels = levels, PeakPop = 120, SettlementName = "Aldwick", EventYear = 287, YearsSince = years,
    };

    public static SiteHistoryInfo Of(SiteKind kind, string sub, ulong id, int levels = 2, bool lair = false, string lairKind = "", MineState mine = MineState.Active, int dead = 0) => new()
    {
        SiteId = id, Kind = kind, Sub = sub, Name = $"{kind} {id}", Levels = levels, MonsterLair = lair, LairKind = lairKind, Mine = mine, Dead = dead, YearsSince = 40, EventYear = 300,
    };

    public static string Draw(SiteMap m, int level)
    {
        var sb = new StringBuilder();
        for (int y = 0; y < m.H; y++)
        {
            for (int x = 0; x < m.W; x++)
            {
                int c = m.Index(level, x, y);
                char ch = m.Tiles[c] switch
                {
                    SiteTile.Rock => ' ', SiteTile.Wall => '#', SiteTile.Floor => m.Air[c] < 600 ? ',' : '.', SiteTile.Door => '+', SiteTile.DoorOpen => '\'', SiteTile.StairsUp => '<',
                    SiteTile.StairsDown => '>', SiteTile.ShaftDown => 'H', SiteTile.ShaftUp => 'h', SiteTile.Pillar => 'o', SiteTile.Container => '$', SiteTile.Water => '~',
                    SiteTile.Rubble => ':', SiteTile.Hole => 'O', SiteTile.Ore => 'v', _ => '?',
                };
                if (m.TrapAt(c) != null) ch = '^';
                foreach (var n in m.Inhabitants) if (n.Level == level && n.X / 1000 == x && n.Y / 1000 == y) ch = n.Monster.Length > 0 ? 'M' : 'p';
                sb.Append(ch);
            }
            sb.Append('\n');
        }
        return sb.ToString();
    }
}

[CollectionDefinition("sites")]
public sealed class SiteCollection : ICollectionFixture<SiteFixture>;
