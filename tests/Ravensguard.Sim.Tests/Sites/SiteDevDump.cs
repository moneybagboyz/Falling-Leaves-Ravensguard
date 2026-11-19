using Ravensguard.Sim.Combat;
using Ravensguard.Sim.Combat.Melee2;
using Ravensguard.Sim.Combat.Wounds;
using Ravensguard.Sim.Sites;
using Ravensguard.Sim.WorldGen.Region;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.Sites;

[Collection("sites")]
public class SiteDevDump(SiteFixture fx, ITestOutputHelper o)
{
    [Fact(Skip = "dev")]
    public void Dump()
    {
        foreach (var i in fx.S.Validate()) o.WriteLine(i.ToString());
        foreach (var h in new[] { SiteFixture.Abbey(), SiteFixture.Of(SiteKind.Cave, "den", 77), SiteFixture.Of(SiteKind.Mine, "iron", 78, 3, mine: MineState.Abandoned), SiteFixture.Of(SiteKind.Barrow, "barrow", 79, 3, true, "draugr") })
        {
            var m = SiteGen.Generate(fx.S, h, 1);
            o.WriteLine($"{h.Kind} {m.Template} {m.W}x{m.H}x{m.Levels} rooms {m.Rooms.Count} containers {m.Containers.Count} traps {m.Traps.Count} folk {m.Inhabitants.Count} loot {m.LootBudgetMites}/{m.LootPlacedMites}");
            for (int l = 0; l < m.Levels; l++) o.WriteLine(SiteFixture.Draw(m, l));
        }
    }

    [Fact]
    public void Crawl()
    {
        var m = SiteGen.Generate(fx.S, SiteFixture.Abbey(), 1);
        var ledger = new WoundLedger(1, 8 * 3600);
        var c = new SiteCrawl(m, fx.S, fx.C, ledger, 8 * 3600, 1);
        var hero = c.Add(new CrawlMember { Person = 7, Name = "Aldric", Kit = new FighterKit { Name = "Aldric", Weapon = new WeaponInstance("arming_sword", "steel"), Skill = 45, Worn = [new("shirt", "linen"), new("gambeson", "linen")] }, SpareTorches = 3 });
        c.ToggleLight(hero);
        var far = m.Rooms.Where(r => r.Level == m.Levels - 1).OrderByDescending(r => r.Index).First();
        int goal = Enumerable.Range(0, m.Cells).Where(i => m.At(i).Level == far.Level && m.Room[i] == far.Index && m.Tiles[i] == SiteTile.Floor).First();
        o.WriteLine($"go {c.GoTo(hero, goal)}");
        for (int s = 0; s < 20 * 600; s++)
        {
            c.Step();
            foreach (var e in c.TakeEvents()) o.WriteLine($"{s} {e.Kind} {e.A} {e.B} {e.Text}");
            if (hero.Cell == goal || c.Ended) break;
            if (hero.Path == null && c.Fight == null && hero.CanAct && !c.GoTo(hero, goal)) { o.WriteLine("no path"); break; }
        }
        o.WriteLine($"end lvl {hero.Level} cell {hero.Cell} wounds {hero.Book.Records.Count} torch {hero.Light.LeftS} known {c.Known.Count} {c.Snapshot}");
        foreach (var w in hero.Book.Records) o.WriteLine($" wound {hero.Plan.Parts[w.Part].Id} cat {w.Cat} grade {w.Grade}");
    }
}
