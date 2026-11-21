using System.Diagnostics;
using System.Reflection;
using Ravensguard.Content;
using Ravensguard.Sim.Abstract;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Law;
using Ravensguard.Sim.Lighting;
using Ravensguard.Sim.People;
using Ravensguard.Sim.Diagnostics;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Society;
using Ravensguard.Sim.World;
using Ravensguard.Sim.Tests.Seamless;
using Ravensguard.Sim.WorldGen;
using Ravensguard.Sim.WorldGen.History;
using Ravensguard.Sim.WorldGen.Region;
using Ravensguard.Sim.Worlds;
using Xunit.Abstractions;

namespace Ravensguard.Sim.Tests.Phase6Overview;

/// <summary>
/// The Phase 6 budgets (overview 9, plan 2.11; Phase 6 close-out G1): every Phase 6 system's cost measured on the reference-sized region (seed 1234, the Phase 6 trail's sixty game days with every system on and every scene),
/// each with a budget test through <see cref="TestBudget"/>. The abstract systems are timed by the ticker (<see cref="RegionWorld.RecordSystemTimings"/>, per call, in the day's first hour and on a season's first day), the links of the
/// hour (talk and standing, law and justice, family, contracts, heists, bonds, faith politics) by a stopwatch round each handler; the figures are the best of two passes (this machine is shared: a pass with another process on the CPU is
/// forgiven, a persistent regression is not, as <see cref="TestBudget.BestPass"/>); the micro budgets (a court day, a sentence's wounds, the witnesses, the standing pass, the Chronicle's opening) are best of five. The
/// numbers of docs/reviews/phase-6-open.md "Close-out G1: budgets measured" come from the output of these tests. The tests run alone after the parallel ones.
/// </summary>
[Collection(TimingCollection.Name)]
public class Phase6BudgetTests(ITestOutputHelper output)
{
    /// <summary>One link's hours: calls, total and worst ticks, and the same for the day's first hour (the daily passes).</summary>
    private sealed class LinkTimes { public long Calls, Ticks, Max, Calls0, Ticks0, Max0; }

    /// <summary>The best (smallest) of the passes for every figure.</summary>
    private sealed class Measured
    {
        public readonly Dictionary<string, double> SysMean = [], SysMax = [], Sys0Mean = [], Sys0Max = [], SysSeasonMean = [], SysSeasonMax = [];
        public readonly Dictionary<string, double> LinkMean = [], LinkMax = [], LinkHourlyMean = [], Link0Mean = [], Link0Max = [];
        public double MaxHourMs = double.MaxValue, MeanHourMs = double.MaxValue, MaxHour0Ms = double.MaxValue, MeanHour0Ms = double.MaxValue;
        public double Load; public int Hours; public string Worst = "";
        public WorldState State = null!; public SeamWorld World = null!;
        public readonly List<string> PassLines = [];
    }

    private static Measured? _cache;

    private static void Min(Dictionary<string, double> d, string k, double v) { if (!d.TryGetValue(k, out var o) || v < o) d[k] = v; }
    private static double Ms(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;

    private static double LoadAverage()
    {
        try { return double.Parse(File.ReadAllText("/proc/loadavg").Split(' ')[0], System.Globalization.CultureInfo.InvariantCulture); } catch { return -1; }
    }

    private static Measured Measure()
    {
        if (_cache != null) return _cache;
        var m = new Measured { World = SeamWorld.Get(1234) };
        var w = m.World; var home = w.BlockOrigin.Offset(1, 1);
        var dir = ContinentSettings.FindBaseContent()!;
        var cs = ContinentSettings.LoadDefaults(dir); cs.Seed = 1234;
        var hs = HistorySettings.LoadDefaults(dir); hs.Seed = 1234;
        m.Load = LoadAverage();
        RegionWorld.RecordSystemTimings = true;
        try
        {
            for (int pass = 0; pass < 2; pass++)
            {
                var region = RegionGenerator.Generate(w.Continent, w.History, home, w.Settings);                      // each pass its own world (a scene changes its region)
                LandmarkPlacer.Place(region, w.History, w.Content);
                var pop = Populator.Populate(region, w.History, w.Content);
                Ravensguard.Sim.Diagnostics.GcSettle.Run();
                double load = LoadAverage();
                var links = new Dictionary<string, LinkTimes>(StringComparer.Ordinal);
                TickerStats stats = null!; Dictionary<string, TimingCell> sys = null!, sys0 = null!, sysS = null!; float[] hm = [], h0 = [];
                var rep = Phase6Trail.Run(w.Content, w.Continent, w.History, cs, hs, w.Settings, home.CellX, home.CellY, pop, null, rt =>
                {
                    var field = typeof(WorldRuntime).GetField("AfterHour", BindingFlags.NonPublic | BindingFlags.Instance)!;
                    var list = ((Delegate)field.GetValue(rt)!).GetInvocationList();
                    field.SetValue(rt, null);
                    foreach (var d in list)
                    {
                        var del = (Action<long>)d; string name = d.Target?.GetType().Name ?? d.Method.Name;
                        var t = links[name] = new LinkTimes();
                        typeof(WorldRuntime).GetEvent("AfterHour")!.AddEventHandler(rt, (Action<long>)(h =>
                        {
                            long t0 = Stopwatch.GetTimestamp(); del(h); long dt = Stopwatch.GetTimestamp() - t0;
                            t.Calls++; t.Ticks += dt; if (dt > t.Max) t.Max = dt;
                            if (h % 24 == 0) { t.Calls0++; t.Ticks0 += dt; if (dt > t.Max0) t.Max0 = dt; }
                        }));
                    }
                }, (rt, st) =>
                {
                    stats = st.HomeRecords!.Ticker.Stats;
                    sys = new(stats.Systems, StringComparer.Ordinal); sys0 = new(stats.SystemsHour0, StringComparer.Ordinal); sysS = new(stats.SystemsSeasonDay, StringComparer.Ordinal);
                    hm = stats.HourMs.ToArray(); h0 = stats.Hour0Ms.ToArray();
                    var wh = stats.WorstHour; m.Worst = $"hour {wh.Hour} {wh.Ms:F1} ms: {wh.Systems}";
                });
                foreach (var (k, c) in sys) { Min(m.SysMean, k, c.MeanMs); Min(m.SysMax, k, c.MaxMs); }
                foreach (var (k, c) in sys0) { Min(m.Sys0Mean, k, c.MeanMs); Min(m.Sys0Max, k, c.MaxMs); }
                foreach (var (k, c) in sysS) { Min(m.SysSeasonMean, k, c.MeanMs); Min(m.SysSeasonMax, k, c.MaxMs); }
                foreach (var (k, t) in links)
                {
                    Min(m.LinkMean, k, Ms(t.Ticks) / Math.Max(1, t.Calls)); Min(m.LinkMax, k, Ms(t.Max));
                    Min(m.LinkHourlyMean, k, Ms(t.Ticks - t.Ticks0) / Math.Max(1, t.Calls - t.Calls0));
                    Min(m.Link0Mean, k, Ms(t.Ticks0) / Math.Max(1, t.Calls0)); Min(m.Link0Max, k, Ms(t.Max0));
                }
                m.MaxHourMs = Math.Min(m.MaxHourMs, hm.Max()); m.MeanHourMs = Math.Min(m.MeanHourMs, hm.Average());
                m.MaxHour0Ms = Math.Min(m.MaxHour0Ms, h0.Max()); m.MeanHour0Ms = Math.Min(m.MeanHour0Ms, h0.Average());
                m.Hours = hm.Length; m.State = rep.State!;
                m.PassLines.Add($"pass {pass}: load average {load:F2} at the start; worst hour {hm.Max():F1} ms, mean {hm.Average():F2} ms; hour 0 worst {h0.Max():F1}, mean {h0.Average():F2} ms; {m.Worst}");
            }
        }
        finally { RegionWorld.RecordSystemTimings = false; }
        return _cache = m;
    }

    // ------------------------------------------------------------------ the abstract tick and the hours' links (overview 9.2)

    [Fact]
    public void P6_TheAbstractSystemsAndLinks_StayInTheirBudgets_AndTheWorstHourUnderTheCap_WithEverySystemOn()
    {
        var m = Measure();
        output.WriteLine($"{m.Hours} hours of the Phase 6 trail (seed 1234, every system on); load average {m.Load:F2} when measuring began");
        foreach (var l in m.PassLines) output.WriteLine(l);
        output.WriteLine($"best of the passes: worst hour {m.MaxHourMs:F1} ms (cap 150), mean hour {m.MeanHourMs:F2} ms, day-first hour: worst {m.MaxHour0Ms:F1} ms, mean {m.MeanHour0Ms:F2} ms");
        foreach (var k in new[] { "unrest", "disease", "faith", "fire", "politics", "rule", "holdings", "land", "captivity" })
            if (m.SysMean.ContainsKey(k)) output.WriteLine($"system {k}: per call mean {m.SysMean[k]:F4} ms, max {m.SysMax[k]:F2} ms; day-first hour mean {m.Sys0Mean.GetValueOrDefault(k):F4} ms, max {m.Sys0Max.GetValueOrDefault(k):F2}; season day mean {m.SysSeasonMean.GetValueOrDefault(k):F4} ms, max {m.SysSeasonMax.GetValueOrDefault(k):F2}");
        foreach (var (k, v) in m.LinkMean.OrderBy(x => x.Key)) output.WriteLine($"link {k}: hour mean {v:F4} ms (outside the day's first hour {m.LinkHourlyMean[k]:F4}), worst {m.LinkMax[k]:F2} ms; day-first hour mean {m.Link0Mean[k]:F4} ms, worst {m.Link0Max[k]:F2}");

        // the daily passes (overview 9.2: hour 0 additions, at most 5 ms on a normal day): unrest 1, illnesses and the plague 0.6 (3 in an epidemic: this run has the plague), holy days 2, prisoners and sentences with the law's day 0.2
        Assert.True(m.Sys0Mean["unrest"] <= TestBudget.Ms(1), $"unrest {m.Sys0Mean["unrest"]:F3} ms");
        Assert.True(m.Sys0Mean["disease"] <= TestBudget.Ms(3), $"disease {m.Sys0Mean["disease"]:F3} ms");
        Assert.True(m.Sys0Mean["faith"] <= TestBudget.Ms(2), $"faith {m.Sys0Mean["faith"]:F3} ms");
        double law0 = m.Link0Mean["LawLink"];                                                // the crime reports, the bounties and the courts of a day, with NPC crime at hour 4 outside it
        Assert.True(law0 <= TestBudget.Ms(2), $"law {law0:F3} ms");
        Assert.True(m.Link0Mean["BondLink"] <= TestBudget.Ms(3), $"bonds {m.Link0Mean["BondLink"]:F3} ms");
        Assert.True(m.Link0Mean["FaithPoliticsLink"] <= TestBudget.Ms(1), $"faith politics {m.Link0Mean["FaithPoliticsLink"]:F3} ms");
        Assert.True(m.Link0Mean["HeistLink"] <= TestBudget.Ms(0.5) && m.Link0Mean["FamilyLink"] <= TestBudget.Ms(0.5));
        double total0 = m.Sys0Mean["unrest"] + m.Sys0Mean["disease"] + m.Sys0Mean["faith"] + law0 + m.Link0Mean["BondLink"] + m.Link0Mean["FaithPoliticsLink"] + m.Link0Mean["HeistLink"] + m.Link0Mean["FamilyLink"];
        output.WriteLine($"the Phase 6 additions of a day's first hour, summed: {total0:F2} ms (budget 5 ms a normal day, 8 on a holy day in an epidemic)");
        Assert.True(total0 <= TestBudget.Ms(8), $"{total0:F2} ms");

        // hourly: the contracts' scanner (2 ms), abstract fires (0.3 ms), the lords' brain an hour (2 ms), the rule's steps of a season's first day (10 ms a call)
        Assert.True(m.LinkHourlyMean["ContractLink"] <= TestBudget.Ms(2), $"contracts {m.LinkHourlyMean["ContractLink"]:F3} ms an hour");
        Assert.True(m.SysMean["fire"] <= TestBudget.Ms(0.3), $"fire {m.SysMean["fire"]:F3} ms");
        Assert.True(m.SysMean["politics"] <= TestBudget.Ms(2), $"politics {m.SysMean["politics"]:F3} ms");
        Assert.True(m.SysSeasonMean["rule"] <= TestBudget.Ms(10), $"rule {m.SysSeasonMean["rule"]:F3} ms");
        // the systems the overview has no line for: their own budgets, about five times what they cost when they were first measured
        Assert.True(m.SysMean["holdings"] <= TestBudget.Ms(0.5) && m.SysMean["land"] <= TestBudget.Ms(1), $"holdings {m.SysMean["holdings"]:F3}, land {m.SysMean["land"]:F3} ms");
        Assert.True(m.LinkMean["FamilyLink"] <= TestBudget.Ms(1) && m.LinkMean["HeistLink"] <= TestBudget.Ms(0.5), $"family {m.LinkMean["FamilyLink"]:F3}, heists {m.LinkMean["HeistLink"]:F3} ms an hour");
        // the whole hour: under the 150 ms cap, in the worst hour of the sixty days, every Phase 2 to 6 system on (the home region's ticker and the hour's links)
        Assert.True(m.MaxHourMs <= TestBudget.Ms(150), $"the worst hour {m.MaxHourMs:F1} ms: {m.Worst}");
    }

    // ------------------------------------------------------------------ the standing pass, the court, the witnesses, the Chronicle

    private static double BestOf(int passes, Func<double> pass)
    {
        double best = double.MaxValue;
        for (int i = 0; i < passes; i++) { Ravensguard.Sim.Diagnostics.GcSettle.Run(); best = Math.Min(best, pass()); }
        return best;
    }

    [Fact]
    public void P6_TheStandingDailyPass_IsUnderAMillisecond_OnTheBookOfSixtyDays()
    {
        var m = Measure(); var st = m.State; var w = m.World;
        var deeds = DeedTable.From(w.Content);
        Assert.NotNull(st.Standing);
        int day0 = (int)(st.Clock.Now.TotalDays);
        var groups = new RegionGroups(st.Population.Region, st.Population.People, day0, Ravensguard.Sim.Faith.FaithWiring.ReligionsOf(st));
        for (int i = 0; i < 3; i++) st.Standing!.Daily(deeds, groups, day0 + 1000 + i);                                     // warm
        int d = day0 + 2000;
        double ms = BestOf(5, () => { var sw = Stopwatch.StartNew(); for (int i = 0; i < 20; i++) st.Standing!.Daily(deeds, groups, d++); return sw.Elapsed.TotalMilliseconds / 20; });
        output.WriteLine($"standing: {st.Standing!.Deeds.Count} deeds, {st.Standing.Rows.Count} rows; the day's pass {ms:F3} ms (budget 1 ms)");
        Assert.True(ms <= TestBudget.Ms(1), $"{ms:F3} ms");
    }

    /// <summary>Holds <paramref name="n"/> thieves of the home town for the next court day: crime, report and arrest, as the NPC watch does.</summary>
    private static (LawWorld W, CrimeBook Cb, JusticeBook Jb, int Day, long Sec, List<EntityId> Held) Hold(Measured m, int n, int skip)
    {
        var st = m.State; var pt = st.Population.People; int town = pt.HomeSettlement[st.Hero!.PersonIndex];
        var w = LawWorld.From(st, m.World.Content); var cb = st.Law ??= new CrimeBook(); var jb = st.Justice ??= new JusticeBook();
        int day = (int)st.Clock.Now.TotalDays + 3 + skip; while (!Court.IsCourtDay(w, day)) day++;
        long sec = (day - 1) * 86_400L + 10 * 3600;
        var guards = new HashSet<int>(Guards.Of(pt, town)); int watch = Guards.Of(pt, town)[0];
        var used = new HashSet<ulong>(jb.Gaol.Keys);
        var people = Enumerable.Range(0, pt.Count).Where(i => pt.IsAlive(i) && pt.HomeSettlement[i] == town && !guards.Contains(i) && pt.AgeYears(i, day) >= 18 && i != st.Hero.PersonIndex && !used.Contains(pt.IdOf(i).Value)).Skip(skip * n).Take(n + 1).ToList();
        var held = new List<EntityId>();
        for (int k = 0; k < n; k++)
        {
            var who = pt.IdOf(people[k]);
            var rec = CrimeSystem.Commit(w, cb, new ActSpec("theft", who, pt.IdOf(people[k + 1]), town, sec, 300, 0, "ch_note_crime_greed"), [new WitnessRow(pt.IdOf(watch).Value, WitnessRow.KnewIt)], ActorLook.None);
            CrimeSystem.Report(w, rec, pt.IdOf(watch), sec + 600);
            Gaol.Arrest(w, cb, jb, who, town, sec + 1200, inAct: false, pt.IdOf(watch));
            held.Add(who);
        }
        return (w, cb, jb, day, day * 86_400L + 8 * 3600, held);
    }

    [Fact]
    public void P6_ACourtDaySitting_IsUnderTwoMillisecondsAFief_AndASentencesWoundsUnderATenthOfOne()
    {
        var m = Measure();
        double best = double.MaxValue; int cases = 0;
        for (int pass = 0; pass < 5; pass++)
        {
            var (w, cb, jb, day, sec, held) = Hold(m, 8, pass);
            Ravensguard.Sim.Diagnostics.GcSettle.Run();
            var sw = Stopwatch.StartNew(); int decided = Court.Sit(w, cb, jb, day, sec); double ms = sw.Elapsed.TotalMilliseconds;
            cases = decided; best = Math.Min(best, ms);
        }
        output.WriteLine($"a court day with 8 held ({cases} decided in the fief): best sitting {best:F3} ms (budget 2 ms a fief)");
        Assert.True(cases >= 1);
        Assert.True(best <= TestBudget.Ms(2), $"{best:F3} ms");

        // the wounds of a flogging of twenty lashes (two cuts) through the wound book
        var st = m.State; var w2 = LawWorld.From(st, m.World.Content); var pt = st.Population.People; int town = pt.HomeSettlement[st.Hero!.PersonIndex];
        var people = Enumerable.Range(0, pt.Count).Where(i => pt.IsAlive(i) && pt.HomeSettlement[i] == town && i != st.Hero.PersonIndex).Skip(400).Take(40).Select(i => pt.IdOf(i)).ToList();
        long sec2 = st.Clock.Now.Seconds + 3600;
        foreach (var p in people.Take(5)) Bodily.Flog(w2, p, 20, sec2, 0);                                                      // warm
        double flog = BestOf(5, () => { var sw = Stopwatch.StartNew(); for (int i = 5; i < people.Count; i++) Bodily.Flog(w2, people[i], 20, sec2 + i, 0); return sw.Elapsed.TotalMilliseconds / (people.Count - 5); });
        output.WriteLine($"a flogging's wounds through the wound book: {flog:F4} ms (budget 0.1 ms)");
        Assert.True(flog <= TestBudget.Ms(0.1), $"{flog:F4} ms");
    }

    private sealed class Dusk : IWitnessScene
    {
        public DayPhase Phase => DayPhase.Day; public bool HeavyRain => false;
        public bool Los(Cell3 a, Cell3 b) => true; public bool Lit(Cell3 c) => false; public bool Indoor(Cell3 c) => false;
    }

    [Fact]
    public void P6_TheSeenOrUnseenRule_ForATownsWorthOfWatchers_IsUnderFiftyMicroseconds()
    {
        var s = new LawSettingsDef { Id = "law" };
        var obs = Enumerable.Range(0, 12).Select(i => new Observer { Person = EntityId.Make(IdKind.Person, (ulong)(10 + i)), At = new Cell3(200 + i * 3, 200 + i, 0) }).ToList();
        var actor = new WitnessActor(EntityId.None, new Cell3(200, 200, 0), false, 0, false, false, 0, ActorLook.None);
        var scene = new Dusk();
        for (int i = 0; i < 2000; i++) Witnessing.SeenCount(s, scene, actor, obs);                                              // warm
        double ms = BestOf(5, () => { var sw = Stopwatch.StartNew(); for (int i = 0; i < 5000; i++) Witnessing.SeenCount(s, scene, actor, obs); return sw.Elapsed.TotalMilliseconds / 5000; });
        long before = GC.GetAllocatedBytesForCurrentThread(); for (int i = 0; i < 1000; i++) Witnessing.SeenCount(s, scene, actor, obs); long bytes = (GC.GetAllocatedBytesForCurrentThread() - before) / 1000;
        output.WriteLine($"seen or unseen, 12 watchers: {ms * 1000:F2} us a call (budget 50 us at 5 Hz); {bytes} bytes allocated a call");
        Assert.True(ms <= TestBudget.Ms(0.05), $"{ms * 1000:F2} us");
    }

    [Fact]
    public void P6_TheChroniclesOpening_OnTwentyThousandEntries_IsUnderTwentyMilliseconds()
    {
        var m = Measure(); var st = m.State;
        int have = st.Chronicle.Count; int want = 20_000;
        for (int i = have; i < want; i++)
            st.Chronicle.Add(new ChronicleEntry(st.Ids.Allocate(IdKind.Event), new GameTime(1_000_000L + i * 3600L), "theft", [st.Population.People.IdOf(i % Math.Max(1, st.Population.People.Count))], [new Cause(CauseKind.Crime, null, "", "ch_note_crime_greed")],
                new Dictionary<string, string>(), "ch_crime_theft", [ChronParam.Person("doer", st.Population.People.IdOf(i % Math.Max(1, st.Population.People.Count))), ChronParam.Mites("value", 240)]));
        var types = ChronicleTypes.From(m.World.Content);
        int home = st.Population.People.HomeSettlement[st.Hero!.PersonIndex];
        double Open()
        {
            var sw = Stopwatch.StartNew();
            var view = ChronicleView.Of(st, home);
            var b = new ChronicleBrowser(view, types, e => ChronicleReader.Line(e, types, view, P6Game.Tr, g => g));
            _ = b.Rows.Count;
            return sw.Elapsed.TotalMilliseconds;
        }
        Open();                                                                                                                 // warm
        double ms = BestOf(5, Open);
        output.WriteLine($"the Chronicle screen's opening on {st.Chronicle.Count} entries (had {have} after sixty days): {ms:F1} ms (budget 20 ms)");
        Assert.True(ms <= TestBudget.Ms(20), $"{ms:F1} ms");
    }

    // ------------------------------------------------------------------ memory and saves (overview 9.3)

    [Fact]
    public void P6_ThePhase6Sections_AtTheEndOfSixtyDays_AreFarUnderTheirBudgetOf350Kilobytes()
    {
        var m = Measure();
        var file = WorldSave.Capture(m.State, "h", "0.1");
        string[] names = ["medicine", "standing", "law", "bondage", "family", "faith", "fiefs", "diplomacy", "unrest", "contracts", "fire", "disease", "heists", "justice"];
        long total = 0;
        foreach (var n in names) if (file.Sections.TryGetValue(n, out var b)) { total += b.Length; output.WriteLine($"section {n}: {b.Length / 1024.0:F1} KB"); }
        output.WriteLine($"the Phase 6 sections together: {total / 1024.0:F1} KB (budget 350 KB for standing, law, bondage, family, faith, fiefs, diplomacy, unrest and contracts, a detailed region); the whole save {SaveCodec.Encode(file).Length / 1024.0:F0} KB, the chronicle {file.Sections[WorldSave.ChronicleSection].Length / 1024.0:F0} KB");
        Assert.True(total <= 350 * 1024, $"{total / 1024.0:F1} KB");
    }
}
