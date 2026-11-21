using System;
using System.Linq;
using Ravensguard.Game.UI;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Production;
using Ravensguard.Sim.Production.Industry;

namespace Ravensguard.Game.World;

/// <summary>
/// The street view's side of the Industry tab (P7-IN3): the tab itself is fed from <c>RefreshBusiness</c> (StreetView.Business.cs) and sends its commands through <c>IndustryAct</c>. This file holds only the dev shot
/// (--ng-shot industry, or --shot-panel industry): an iron works is founded for the hero in the town he stands in, crewed from the idle, its premises set and a caravan order given, and a few works days are run at the barrier so the balance sheet has rows.
/// </summary>
public partial class StreetView
{
    /// <summary>Dev: stages a hero-owned iron works with a crew and a few days' books, then opens the Ledger on the Industry tab.</summary>
    public void DebugIndustry()
    {
        if (Runtime is not { } rt || _heroLink == null) { _router?.ShowToast(L.T("MAP_NONE")); return; }
        int day = (int)(HeroNow() / 86400); int town = Math.Max(0, HeroSettlement());
        string note = rt.AtBarrier(() =>
        {
            var st = rt.State; var prod = st.Production; var ow = OwnOf(rt); var e = st.Economy;
            if (prod == null || ow == null || e == null || st.Hero == null || st.Content == null) return "no world";
            var iw = IndustryWiring.For(st, st.Content); int hero = st.Hero.PersonIndex;
            e.Ledger.Mint(ow.HeroPurse, Coin.FromCrowns(3000), SourceReason.Seed);                       // dev faucet (the shots only)
            var found = iw.Found("iron", hero, town, ow.HeroPurse, 250_000, day);
            if (!found.Ok) return "found: " + found.Key;
            var w = st.Industry!.Works.Last();
            var pt = st.Population.People;
            var idle = Enumerable.Range(0, pt.Count).Where(i => pt.Status[i] == PersonStatus.Alive && pt.HomeSettlement[i] == town && pt.AgeYears(i, day) is >= 18 and < 55 && i != hero
                && !prod.Businesses.Any(x => x.Owner == i || x.Staff.Contains(i))).ToList();
            int next = 0;
            for (int s = 0; s < w.Stages.Count; s++)
            {
                var k = iw.KindOf(w.Stages[s]); var b = iw.BusinessOf(w.Stages[s])!; var t = k.Tier(1);
                int need = s == 4 ? 1 : t.CrewMin;                                                       // packing runs on one pair of hands
                for (int n = 0; n < need && next < idle.Count; n++, next++)
                {
                    int p = idle[next]; bool prim = t.SupportPm == 0 || n * (1000 + t.SupportPm) < need * 1000;
                    pt.Occupation[p] = prim ? k.Primary : k.Support;
                    pt.SetSkill(p, 0, k.Skill == Skill.None ? Skill.Farming : k.Skill, 50);
                    prod.Employ(b, p, prim ? k.Primary : k.Support);
                }
            }
            var reg = st.Population.Region; int cx = reg.Settlements[town].CenterX, cy = reg.Settlements[town].CenterY;
            iw.SetSite(w.Stages[0].Business, cx - 40, cy - 40, cx - 10, cy - 10);
            iw.SetRoute(w.Stages[^1].Business, Routing.Gate); iw.SetRoute(w.Stages[3].Business, Routing.Gate);
            int other = Enumerable.Range(0, reg.Settlements.Count).FirstOrDefault(i => i != town);
            iw.SetCaravanOrder(w.Stages[3].Business, DestKind.Town, other, 2, 2, 2, false);
            for (int d = 4; d >= 1; d--) iw.Day(w, day - d, GameTime.FromGameHours((long)(day - d) * 24 + 9));
            return $"works founded with {next} hands; stages {string.Join(", ", w.Stages.Select(s => iw.Status(s.Business)?.Stop.ToString() ?? "-"))}";
        }).GetAwaiter().GetResult();
        Godot.GD.Print("[industry] dev works: " + note);
        RefreshBusiness();
        OpenLedger(Ravensguard.Sim.Production.LedgerTab.Industry);
    }
}
