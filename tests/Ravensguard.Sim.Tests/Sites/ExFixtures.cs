using Ravensguard.Sim.Combat;
using Ravensguard.Sim.Combat.Melee2;
using Ravensguard.Sim.Sites;

namespace Ravensguard.Sim.Tests.Sites;

/// <summary>The crypt crawl of EX-12 (the Phase 4 done-when): the abbey's crypt with its traps and one guard who is no draugr, and a lightly kitted hero by torchlight.</summary>
public static class ExFixtures
{
    /// <summary>The crypt's people: one grave robber standing guard in the deepest level's far room (the crypt).</summary>
    public static Inhabitant OneGuard(SiteFixture fx, SiteMap m)
    {
        m.Inhabitants.Clear();
        var crypt = Crypt(m);
        int cell = Enumerable.Range(0, m.Cells).First(i => m.Room[i] == crypt.Index && m.Tiles[i] == SiteTile.Floor && m.TrapAt(i) == null);
        var (l, x, y) = m.At(cell);
        var g = new Inhabitant { Id = SiteGen.FolkId(m.SiteId, 0), Role = "grave_robber", Kit = "grave_robber", Name = "Osric Pale", Home = cell, Skill = 42, Hostile = true, State = InhabitantState.Home };
        TestMaps.Place(g, x, y, l);
        m.Inhabitants.Add(g);
        return g;
    }

    public static PlacedRoom Crypt(SiteMap m) => m.Rooms.Where(r => r.Level == m.Levels - 1 && r.Template != "stairs_down").OrderByDescending(r => r.Index).First();

    public static int CryptCell(SiteMap m) { var c = Crypt(m); return Enumerable.Range(0, m.Cells).First(i => m.Room[i] == c.Index && m.Tiles[i] == SiteTile.Floor); }

    /// <summary>The hero of the crawl: a sword, a shirt and a tunic, middling skill, poor eyes for traps, three spare torches.</summary>
    public static CrawlMember Hero() => new()
    {
        Person = 7, Name = "Aldric",
        Kit = new FighterKit { Name = "Aldric", Person = 7, Weapon = new WeaponInstance("arming_sword", "iron"), Skill = 50, Worn = [new("shirt", "linen"), new("tunic", "wool")] },
        SpareTorches = 3, Perception = 40,
    };
}
