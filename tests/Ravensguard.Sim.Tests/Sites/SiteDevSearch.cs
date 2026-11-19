using Ravensguard.Sim.Combat.Wounds;
using Ravensguard.Sim.Sites;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.Sites;

[Collection("sites")]
public class SiteDevSearch(SiteFixture fx, ITestOutputHelper o)
{
    [Fact(Skip = "dev")]
    public void Search()
    {
        for (ulong seed = 1; seed <= 200; seed++)
        {
            var m = SiteGen.Generate(fx.S, SiteFixture.Abbey(4401), seed);
            ExFixtures.OneGuard(fx, m);
            var c = TestMaps.Crawl(fx, m, sec: 9 * 3600, seed: seed);
            var h = c.Add(ExFixtures.Hero()); c.ToggleLight(h);
            var far = m.Rooms.Where(r => r.Level == m.Levels - 1 && r.Template != "stairs_down").OrderByDescending(r => r.Index).First();
            int goal = Enumerable.Range(0, m.Cells).First(i => m.At(i).Level == far.Level && m.Room[i] == far.Index && m.Tiles[i] == SiteTile.Floor);
            c.GoTo(h, goal);
            string what = ""; int traps = 0, fights = 0;
            for (int s = 0; s < 20 * 900 && !h.Dead; s++)
            {
                c.Step();
                var fw = c.Fight;
                if (false && fw != null && fw.Tick == 1) fw.OnBlow = (att, tgt, r, st) => o.WriteLine($"    blow {att.Name}->{tgt.Name} hit {r.Hit} wounds {r.WoundCount} part {r.Part} m {r.InitialMilli} sp {st.SituationPoints} att {st.Attack} type {st.Weapon.Attacks[st.Attack].Type}");
                if (false) o.WriteLine("   t" + fw.Tick + " " + string.Join(" ", fw.Fighters.Select(x => $"{x.Name}:{x.Act}@{x.X},{x.Y} tgt{x.Brain.Target} pl{x.Player} st{x.Stance.Id}")));
                foreach (var e in c.TakeEvents()) { if (e.Kind == CrawlEventKind.TrapTriggered) traps++; if (e.Kind == CrawlEventKind.FightStarted) fights++; if (e.Kind == CrawlEventKind.FightEnded && fights < 0) o.WriteLine($"  end {e.A} at {s} {c.LastFightEnd} " + string.Join(",", m.Inhabitants.Select(n => n.State + ":" + n.Name))); }
                if (fw != null && fw.IsOver && fights < 4) o.WriteLine("   fight over tick " + fw.Tick + " " + string.Join(",", fw.Fighters.Select(f => f.Name + ":" + f.Act + "@" + f.X + "," + f.Y)));
                var bs = h.Book.Function(h.Plan);
                for (int p = 0; p < bs.Parts.Length && what.Length == 0; p++) if (bs.Parts[p].TendonCut) what = "tendon " + h.Plan.Parts[p].Id + " at " + s / 20;
                foreach (var w in h.Book.Records) if (what.Length == 0 && w.Cat == WoundCat.Bone && w.Grade >= 2 && h.Plan.Parts[w.Part].Limb == "leg") what = "broken " + h.Plan.Parts[w.Part].Id + " at " + s / 20;
                if (h.Cell == goal && c.Fight == null) break;
                if (c.Fight == null && h.Path == null && h.CanAct && !c.GoTo(h, goal)) break;
            }
            if (false) foreach (var w in h.Book.Records) o.WriteLine($"   {h.Plan.Parts[w.Part].Id} {w.Cat} g{w.Grade} s{w.Structure} {(w.Structure >= 0 ? h.Plan.Parts[w.Part].Structures[w.Structure].Kind.ToString() : "")} cut{h.Book.Function(h.Plan).Parts[w.Part].TendonCut}");
            o.WriteLine($"seed {seed}: {what} traps {traps} fights {fights} lvl {h.Level} dead {h.Dead} wounds {h.Book.Records.Count} {c.LastFightEnd}");
        }
    }
}
