using Ravensguard.Sim.Core;
using Ravensguard.Sim.Growth;
using Ravensguard.Sim.Production;
using Ravensguard.Sim.Tests.Production;
using Xunit;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.EconomyAudit;

/// <summary>
/// P8-EA-1 (the economy and growth audit, overview 2.6; findings in docs/reviews/phase-8-economy-audit.md): the finite resources turn over and the chronicle gives the causes. A mine whose body
/// is worked out closes (EA8_F3); prospectors find new ore bodies and new mines open where the metal is wanted (EA8_F2); over-cut woods make timber dear and the entry names the cause (EA8_F4).
/// On the shared headless world of seed 1234 (<see cref="SoakWorld"/>).
/// </summary>
public class Ea1ResourceTests(ITestOutputHelper output)
{
    private static bool HasNote(ChronicleEntry e, string key) => e.Causes.Any(c => c.NoteKey == key);

    [Fact]
    public void EA8_F3_AWorkedOutMine_Closes_LetsItsHandsGo_AndTheChronicleSaysWhy()
    {
        using var w = SoakWorld.Create();
        var b = w.Sys.Businesses.First(x => x.Mine >= 0 && x.State == BusinessState.Open);
        var m = w.Sys.Mines[b.Mine];
        m.Mine(m.RemainingG);                                                                       // the body dug out
        w.Run(TimeConst.DaysPerSeason + 1);
        Assert.Equal(BusinessState.Closed, b.State);
        Assert.Empty(b.Staff);
        var e = Assert.Single(w.Sim.Chronicle.ByType("mine_worked_out"));
        Assert.Contains(b.Id, e.Subjects);
        Assert.True(HasNote(e, "ch_why_ore_gone"));
        Assert.Equal(m.Mineral.ToString(), e.Data["mineral"]);
        Assert.True(w.Econ.Ledger.Audit().IsConserved && w.Econ.MassAuditHolds());
        // a mine with ore left is not touched
        Assert.All(w.Sys.Businesses.Where(x => x.Mine >= 0 && x != b), x => Assert.Equal(BusinessState.Open, x.State));
    }

    private sealed record ProspectRun(List<(string Mineral, int Tonnes, int Settlement, int Staff, long Mined)> Found, int Veins, int Opened, bool Chained, bool CoinOk, bool MassOk, string Sig);

    private static ProspectRun Prospect(int years)
    {
        using var w = SoakWorld.Create();
        w.Run(years * TimeConst.DaysPerYear + 1);
        var ch = w.Sim.Chronicle; var veins = ch.ByType("vein_found").ToList(); var opened = ch.ByType("mine_opened").ToList();
        bool chained = opened.All(o => o.Causes.Any(c => c.EventId is { } id && veins.Any(v => v.Id == id))) && veins.All(v => v.Causes.Any(c => c.NoteKey.StartsWith("ch_why_prospect_", StringComparison.Ordinal)));
        var found = new List<(string, int, int, int, long)>();
        foreach (var b in w.Sys.Businesses)
        {
            if (b.Mine < 0 || !GrowthSystem.IsFoundMine(w.Sys.Mines[b.Mine])) continue;
            var m = w.Sys.Mines[b.Mine];
            found.Add((m.Mineral.ToString(), (int)(m.InitialG / 1_000_000), b.Settlement, b.Staff.Count, m.MinedG));
        }
        var au = w.Econ.Ledger.Audit();
        return new ProspectRun(found, veins.Count, opened.Count, chained, au.IsConserved, w.Econ.MassAuditHolds(), string.Join(";", found) + "/" + au.InPurses.Mites);
    }

    [Fact]
    public void EA8_F2_Prospectors_FindNewOreBodies_AndNewMinesOpen_WorkedByHiredHands_Deterministically()
    {
        var a = Prospect(4);
        output.WriteLine($"veins {a.Veins}, opened {a.Opened}: {string.Join("; ", a.Found)}");
        Assert.InRange(a.Veins, 1, 4);                                                               // at most one find a year
        Assert.Equal(a.Veins, a.Opened);
        Assert.True(a.Chained, "each new mine is caused by its find, and each find by the want that sent the prospector");
        Assert.All(a.Found, f => Assert.InRange(f.Tonnes, GrowthSystem.FoundTonnesMin, GrowthSystem.FoundTonnesMax));
        Assert.Contains(a.Found, f => f.Staff > 0 && f.Mined > 0);                                  // the labour market crews it and the ore comes out of the body
        Assert.True(a.CoinOk && a.MassOk);
        var b = Prospect(4);
        Assert.Equal(a.Sig, b.Sig);
    }

    private static (int MedianPm, int Entries, List<string> Places, long LogsMadeT) Woods(bool overcut, int days, ITestOutputHelper? log = null)
    {
        using var w = SoakWorld.Create();
        var lay = w.Layer; var logs = w.Econ.Goods.IdOf("logs");
        if (overcut) for (int i = 0; i < lay.Count; i++) if (lay.KindOf(i) == ResourceKind.Wood) lay.SetStock(i, lay.CapG(i) * 15 / 100);     // every wood of the region cut to 15 percent of its stand
        int Median() { var x = w.Markets.AllBooks().Where(bk => bk.Merchants.Count > 0).Select(bk => bk.MidRatioPermille(logs)).Order().ToList(); return x[x.Count / 2]; }
        w.Run(days, (d, _) => { if (d % TimeConst.DaysPerSeason == 0) log?.WriteLine($"  {(overcut ? "cut" : "control")} day {d}: logs median {Median()} pm, logs made {w.Econ.Mass.TotalCreated(logs) / 1_000_000} t"); });
        var r = w.Markets.AllBooks().Where(bk => bk.Merchants.Count > 0).Select(bk => bk.MidRatioPermille(logs)).Order().ToList();
        var e = w.Sim.Chronicle.ByType("woods_overcut").ToList();
        Assert.All(e, x => Assert.True(HasNote(x, "ch_why_overcut")));
        return (r[r.Count / 2], e.Count, e.Select(x => x.Data["settlement"] + ":" + x.Data["stock_pct"] + "%:" + x.Data["price_pm"]).ToList(), w.Econ.Mass.TotalCreated(logs) / 1_000_000);
    }

    [Fact]
    public void EA8_F4_OvercutWoods_MakeTimberDear_AndTheChronicleNamesTheCause()
    {
        int days = 2 * TimeConst.DaysPerYear + 1;
        var control = Woods(false, days, output); var cut = Woods(true, days, output);
        output.WriteLine($"control: logs median {control.MedianPm} pm, entries {control.Entries}; over-cut: logs median {cut.MedianPm} pm, entries {cut.Entries}: {string.Join(", ", cut.Places.Take(12))}");
        Assert.True(cut.Entries > control.Entries, $"entries {cut.Entries} against {control.Entries}");
        Assert.True(cut.LogsMadeT * 2 < control.LogsMadeT, $"logs cut {cut.LogsMadeT} t against {control.LogsMadeT} t: the cutters' yield falls with the stand (resources spec 3.1)");
        Assert.True(cut.MedianPm > control.MedianPm, $"logs {cut.MedianPm} pm against {control.MedianPm}");
        Assert.True(cut.Entries > 0, "the chronicle names the over-cut woods where timber is dear");
    }

}
