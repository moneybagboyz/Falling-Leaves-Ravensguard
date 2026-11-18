using Ravensguard.Sim.Abstract;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.Abstract;

public class Scratch(ITestOutputHelper o)
{
    [Fact]
    public void Probe()
    {
        var w = AbstractFixture.NewWorld(Ravensguard.Sim.People.Vital.VitalRates.Default with { StemJoinMaxOccupancyPct = int.Parse(Environment.GetEnvironmentVariable("STEM") ?? "80") });
        var c0 = AbstractFixture.Census(w);
        o.WriteLine($"start alive {c0.Alive} hh {c0.Households} kids {c0.KidsPct:0.0} elders {c0.ElderPct:0.0}; people rows {w.People.Count}");
        int vac = 0, dw = 0;
        for (int s = 0; s < w.Population.LotBeds.Length; s++) for (int l = 0; l < w.Population.LotBeds[s].Length; l++) if (w.Population.LotBeds[s][l] > 0) { dw++; if (w.Population.LotHousehold[s][l] < 0) vac++; }
        o.WriteLine($"dwellings {dw} vacant {vac}; fiefs {w.Population.Region.Fiefs.Count} settlements {w.Population.Region.Settlements.Count}");
        { var h0=w.Households; long beds=0, mem=0; int[] hist=new int[14]; for(int k=0;k<h0.Count;k++){ beds+=h0.Beds[k]; mem+=h0.MemberCount[k]; hist[Math.Min(13,h0.MemberCount[k]*100/Math.Max(1,(int)h0.Beds[k])/10)]++; } o.WriteLine($"beds {beds} members {mem} avg beds {beds*10/h0.Count/10.0}; crowding deciles {string.Join(",",hist)}"); }
        using var t = new WorldTicker(w, AbstractFixture.Registry());
        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (int y = 1; y <= 100; y++)
        {
            t.AdvanceDays(60);
            if (y % 5 != 0 && y > 3) continue;
            var c = AbstractFixture.Census(w);
            { var p=w.People; long d=w.State.HourDone/24; int wom=0,mar=0,pregn=0,wait=0,single18=0; int crowded=0, hhn=0; long mem=0;
              for (int i=0;i<p.Count;i++){ if(p.Status[i]!=Ravensguard.Sim.People.PersonStatus.Alive) continue; int a=w.AgeOn(i,d); if(p.Sex[i]==Ravensguard.Sim.People.Sex.Female && a>=18&&a<=44){wom++; if(p.Spouse[i]>=0)mar++; if(p.PregnantUntilDay[i]>=0)pregn++;} if(p.Spouse[i]<0&&a>=18&&a<=50) single18++; if(p.MarriageWait[i]>0) wait++; }
              var h=w.Households; for(int k=0;k<h.Count;k++){ if(h.MemberCount[k]==0)continue; hhn++; mem+=h.MemberCount[k]; if(h.MemberCount[k]>h.Beds[k])crowded++; }
              int vacn=0; for(int s2=0;s2<w.Population.LotBeds.Length;s2++) for(int l=0;l<w.Population.LotBeds[s2].Length;l++) if(w.Population.LotBeds[s2][l]>0&&w.Population.LotHousehold[s2][l]<0) vacn++;
              o.WriteLine($"   women18-44 {wom} married {mar} ({100*mar/Math.Max(1,wom)}%) pregnant {pregn}; unmarried 18-50 {single18}; waiting {wait}; hh {hhn} avg size {mem*10/Math.Max(1,hhn)/10.0} crowded {crowded} vacant {vac}"); }
            o.WriteLine($"year {y}: alive {c.Alive} hh {c.Households} kids {c.KidsPct:0.0} elders {c.ElderPct:0.0} births {w.Vital.Births} deaths {w.Vital.Deaths} marr {w.Vital.Marriages} chron {w.Chronicle.Count}  {sw.ElapsedMilliseconds} ms");
        }
        o.WriteLine(t.Stats.Report());
    }
}
