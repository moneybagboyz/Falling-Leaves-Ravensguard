using Ravensguard.Sim.Core;
using Ravensguard.Sim.Farming;
using Ravensguard.Sim.Tests.Economy;
using Ravensguard.Sim.WorldGen.Region;
using Ravensguard.Sim.People;
using Xunit.Abstractions;
namespace Ravensguard.Sim.Tests.Farming;
public class Scratch(ITestOutputHelper o)
{
    [Fact] public void Diag()
    {
        var soak = new FarmingSoak(); var sys = soak.Sys; var F = sys.State.Fields; var P = sys.State.Parcels; int y0 = EconFixture.Pop.NowYear; var E = soak.Econ; var H = EconFixture.Pop.Households;
        var spr = new[] { "barley", "oats", "peas", "beans" }.Select(E.Goods.IdOf).ToArray(); var win = new[] { "wheat", "rye" }.Select(E.Goods.IdOf).ToArray();
        long Stock(IEnumerable<int> hs, ushort[] gs) => hs.Sum(h => gs.Sum(g => E.HouseholdStore[h].Qty(g)));
        var gentry = Enumerable.Range(0, H.Count).Where(h => H.Class[h] == SocialClass.Gentry).ToList(); var others = Enumerable.Range(0, H.Count).Where(h => H.Class[h] != SocialClass.Gentry).ToList();
        long parishSpr = 0, parishWin = 0;
        for (int y = y0; y < y0 + 5; y++)
        {
            for (int d = 1; d <= 60; d++)
            {
                var now = Synth.At(y, d); sys.StepDay(now);
                E.ApplyDailySpoilage(1, now.TotalHours, 10);
                var (w, s) = sys.SeedNeeds();
                // eat like the soak harness does
                typeof(FarmingSoak).GetMethod("Eat", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(soak, [(long)(soak.People * 0.92 * 1100), w, s]);
                if (d is 1 or 14 or 15 or 31 or 35 or 45)
                {
                    parishSpr = Enumerable.Range(0, EconFixture.Pop.Region.Settlements.Count).Sum(si => spr.Sum(g => E.OtherStores[FarmIds.ParishGranary(si)].Qty(g)));
                    int spring = 0, winter = 0, fl = 0; for (int f = 0; f < F.Count; f++) { if (F.Slot[f] == 1 && F.Crop[f] != Crop.Fallow) spring++; if (F.Slot[f] == 2 && F.Crop[f] != Crop.Fallow) winter++; if (F.Slot[f] == 1) fl++; }
                    o.WriteLine($"y{y} d{d}: spring sown {spring}/{fl} winter sown {winter} | house spring-grain {Stock(others, spr) / 1000}t winter-grain {Stock(others, win) / 1000}t; gentry spring {Stock(gentry, spr) / 1000}t winter {Stock(gentry, win) / 1000}t; parish spring {parishSpr / 1000}t; need spring {s.Sum() / 1000}t winter {w.Sum() / 1000}t; debt {sys.State.SeedDebtG.Sum() / 1000}t");
                }
            }
        }
    }
}
