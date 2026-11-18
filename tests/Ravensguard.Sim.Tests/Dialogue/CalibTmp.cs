using Ravensguard.Content;
using Ravensguard.Sim.Dialogue;
using Ravensguard.Sim.Rumours;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.Dialogue;

public class CalibTmp(ITestOutputHelper o)
{
    static double Median(LineBook book, int people, int runs)
    {
        var days = new List<double>();
        for (int run = 0; run < runs; run++)
        {
            var (w, c) = TalkKit.Town(people, seed: (ulong)(1000 + run * 37));
            var id = TalkKit.AddEntry(c, "fire", 100, "Ashby"); var ts = new TalkState(); double found = 99;
            for (long h = 100 * 24 + 7; h < 100 * 24 + 30 * 24; h++)
            {
                TalkSystem.AfterHour(w, ts, book, h);
                if (ts.Rumours.HearingsOf(id).Count * 2 >= people) { found = (h - (100 * 24 + 6)) / 24.0; break; }
            }
            days.Add(found);
        }
        days.Sort(); return days[days.Count / 2];
    }

    [Fact]
    public void Sweep()
    {
        var def = LineBook.Default;
        foreach (int home in new[] { 20, 30 })
        foreach (int work in new[] { 6, 10 })
        foreach (int open in new[] { 14, 17, 20 })
        {
            var soc = def.Settings.SocialPmByHour.Select(v => v * open / 300).ToList();
            var book = new LineBook { Lines = def.Lines, Barks = def.Barks, Dialogues = def.Dialogues, RumourTypes = def.RumourTypes, Settings = def.Settings with { HomePm = home, WorkPm = work, SocialPmByHour = soc } };
            o.WriteLine($"home {home} work {work} open {open}: town {Median(book, 1000, 5):0.0}  village {Median(book, 150, 5):0.0}");
        }
    }
}
