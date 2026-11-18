using Ravensguard.Sim.Autopilot;
using Ravensguard.Sim.Core;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.Autopilot;

public class AutopilotSmoke(ITestOutputHelper output)
{
    [Fact]
    public void Smoke_FiveDaysOfWork()
    {
        using var w = ApWorld.Soak();
        w.Ap.Enable(w.Hero, w.Sec, Directive.Work());
        long c0 = w.Ctx.Coin;
        w.RunHours(24 * 3, h => { var st0 = w.Book.Get(w.Hero)!; var j = w.Table.JobOf(w.Hero); if (h % 3 == 0) output.WriteLine($"h{h} intent {st0.Intent} job {(j == null ? "-" : j.Title + " " + j.State)} cur {st0.CurrentJob.Value} coin {w.Ctx.Coin} next {st0.NextWindow} paused {st0.Paused} busPaused {w.Ap.BusOf(st0).Paused}"); });
        output.WriteLine($"coin {c0} -> {w.Ctx.Coin}; log {w.Book.Log.Lines.Count}");
        foreach (var l in w.Book.Log.Lines.Take(60)) output.WriteLine($"{new GameTime(l.Sec).DayOfYear}d {l.Sec % 86400 / 3600:00}:{l.Sec % 3600 / 60:00} {l.Code} {l.Text}");
        var st = w.Book.Get(w.Hero)!;
        foreach (var s in st.Summaries) output.WriteLine($"day {s.Day}: earned {s.Earned} spent {s.Spent} jobs {s.JobsDone} meals {s.Meals} nutrition {s.NutritionCd}");
    }
}
