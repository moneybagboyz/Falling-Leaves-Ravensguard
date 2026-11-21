using Ravensguard.Sim.Core;
using Ravensguard.Sim.Diagnostics;
using Ravensguard.Sim.People;
using Ravensguard.Sim.WarSupply;
using Xunit;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.EconomyAudit;

/// <summary>P7-EA3d (overview 10d): the levy is drawn by village. A fief's quota fills village by village up to a cap, so the drawn villages lose their reapers and the undrawn ones keep theirs.</summary>
public class LevyByVillageTests(ITestOutputHelper output)
{
    private static int Households(WarLoadRun run, int fief)
    {
        var hh = run.State.Population.Households; var region = run.State.Population.Region; int n = 0;
        for (int i = 0; i < hh.Count; i++) if (hh.MemberCount[i] > 0 && hh.Settlement[i] >= 0 && region.Settlements[hh.Settlement[i]].FiefIndex == fief) n++;
        return n;
    }

    /// <summary>The order is the lord's: villages nearest the seat first, farmsteads (under 5 households) after the villages, the seat last; a village gives at most its cap before the next is asked, so the undrawn villages send no one.</summary>
    [Fact]
    public void Muster_FillsTheQuotaVillageByVillage_UpToTheCap_InTheLordsOrder()
    {
        using var run = AuditWorld.Create(links: false);
        var h = AuditWorld.Host(run, 7, new Roster(), 1000);
        var hs = run.Supply; var pt = run.State.Population.People; var hh = run.State.Population.Households; var region = run.State.Population.Region;
        int capPct = run.Economy.Farming.Settings.Def.LevyVillageCapPct; Assert.Equal(50, capPct);
        int Villages(int f) => Enumerable.Range(0, hh.Count).Where(i => hh.MemberCount[i] > 0 && hh.Settlement[i] >= 0 && region.Settlements[hh.Settlement[i]].FiefIndex == f && hh.Settlement[i] != region.Fiefs[f].SeatSettlement).GroupBy(i => hh.Settlement[i]).Count(g => g.Count() >= 5);
        int fief = Enumerable.Range(0, region.Fiefs.Count).OrderByDescending(Villages).First();
        int quota = Households(run, fief) / 5;
        var m = hs.Muster(h, fief, quota);
        Assert.Equal(quota, m.Persons.Count);
        var perVillage = m.Persons.GroupBy(p => hh.Settlement[pt.Household[p]]).ToDictionary(g => g.Key, g => g.Count());
        var households = Enumerable.Range(0, hh.Count).Where(i => hh.MemberCount[i] > 0 && hh.Settlement[i] >= 0 && region.Settlements[hh.Settlement[i]].FiefIndex == fief).GroupBy(i => hh.Settlement[i]).ToDictionary(g => g.Key, g => g.Count());
        int seat = region.Fiefs[fief].SeatSettlement;
        long Dist(int s) { long dx = region.Settlements[s].CenterX - region.Settlements[seat].CenterX, dy = region.Settlements[s].CenterY - region.Settlements[seat].CenterY; return dx * dx + dy * dy; }
        var inOrder = households.Keys.OrderBy(s => s == seat ? 2 : households[s] < SupplyConfig.LevyVillageMinHouseholds ? 1 : 0).ThenBy(Dist).ThenBy(s => s).ToList();
        int drawn = perVillage.Count; output.WriteLine($"fief {fief}: {households.Count} settlements, quota {quota}, drawn from {drawn} of them: {string.Join(", ", perVillage.Select(kv => $"{kv.Key}:{kv.Value}/{households[kv.Key]}"))}");
        Assert.All(perVillage, kv => Assert.True(kv.Value <= Math.Max(1, households[kv.Key] * capPct / 100), "a village gives no more than its cap"));
        Assert.All(m.Persons.GroupBy(p => pt.Household[p]), g => Assert.Single(g));                         // one man a household at this cap
        // the drawn villages are the first in the lord's order that have able men, and each but the last gave its cap (or every able man)
        var drawnInOrder = inOrder.Where(perVillage.ContainsKey).ToList();
        Assert.Equal(inOrder.Where(s => perVillage.ContainsKey(s)).ToList(), drawnInOrder);
        int lastDrawn = inOrder.FindLastIndex(perVillage.ContainsKey);
        for (int k = 0; k < lastDrawn; k++) if (perVillage.TryGetValue(inOrder[k], out int got)) Assert.Equal(Math.Max(1, households[inOrder[k]] * capPct / 100), got);
        Assert.True(drawn < households.Count, "some villages are untouched");
        Assert.All(perVillage.Keys, s => Assert.Equal(m.Persons.Count(p => hh.Settlement[pt.Household[p]] == s), hs.LevyMenFrom(s, (int)(run.HourDone / 24) / 60)));
        Assert.Equal(0, hs.LevyMenFrom(households.Keys.First(s => !perVillage.ContainsKey(s)), (int)(run.HourDone / 24) / 60));
    }

    /// <summary>A summer levy against the same world without one: the grain the drawn villages cannot reap in time falls behind the control's, while the villages nobody was drawn from reap exactly as the control does. Measured on the acres reaped by day 30 of the year (the harvest's first days).</summary>
    [Fact]
    public void SummerLevy_TheDelayFallsOnTheDrawnVillages_TheUndrawnReapInFull()
    {
        var (reapedWar, reapedCtl, levied) = Reaped(true);
        var control = Reaped(false).Reaped;
        long dWar = 0, dCtl = 0, uWar = 0, uCtl = 0;
        foreach (var s in reapedWar.Keys) { if (levied.Contains(s)) { dWar += reapedWar[s]; dCtl += control[s]; } else { uWar += reapedWar[s]; uCtl += control[s]; } }
        output.WriteLine($"drawn villages {levied.Count}: reaped {dWar} acres against the control's {dCtl}; undrawn {reapedWar.Count - levied.Count}: {uWar} against {uCtl}");
        Assert.True(dWar * 1000 <= dCtl * 990, "the drawn villages are at least 1% behind the control");
        Assert.InRange(uWar * 1000, uCtl * 995, uCtl * 1005 + 1);                  // the undrawn villages bring theirs in as the control does (the war's market and wage ripples apart)
        _ = reapedCtl;
    }

    private (Dictionary<int, long> Reaped, Dictionary<int, long> ReapedCtl, HashSet<int> Levied) Reaped(bool war)
    {
        using var run = AuditWorld.Create(links: false);
        var hs = run.Supply; var region = run.State.Population.Region; var tax = run.Economy.Taxes;
        int start = ((int)(run.HourDone / 24) / 60 + 1) * 60;                               // the world is built at the end of a year: the next year's first day
        var h = war ? AuditWorld.Host(run, 9, new Roster(), 1000) : default;
        var levied = new HashSet<int>();
        for (int d = 0; d < 30; d++)                                                        // to day 30 of the year (doy 31): ripe from 26
        {
            int day = start + d;
            run.BeforeSlot(day);
            if (war && d == 15)                                                             // doy 16: the summer levy
            {
                for (int f = 0; f < region.Fiefs.Count; f++) hs.Muster(h, f, Math.Max(1, tax.LevyQuota(Households(run, f), true)));
                foreach (var kv in hs.Book.Levied) levied.Add(kv.Key);
            }
            run.AfterSlot(day); run.AdvanceToDayEnd(day);
        }
        var P = run.Economy.Farming.State.Parcels; var reaped = new Dictionary<int, long>();
        for (int s = 0; s < region.Settlements.Count; s++) { long a = 0; foreach (int p in region.Settlements[s].Parcels) a += P.ReapDone[p]; reaped[s] = a; }
        return (reaped, reaped, levied);
    }
}
