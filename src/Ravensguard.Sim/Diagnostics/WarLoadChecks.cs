using System.Text;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Worlds;

namespace Ravensguard.Sim.Diagnostics;

/// <summary>
/// WL-13 on the war-load soak (P7-EA3b): two runs of a seed give equal save hashes at Y10 day 60, and save, load, save is byte-identical in every section at Y6 day 20, Y8 day 40 and Y9 day 10 (the last two with the host in the field and the siege on).
/// The caller builds each soak (the scenario, the L3 works) through <paramref name="make"/>; the checks hang on <see cref="WarLoadSoak.AtDayStart"/>.
/// </summary>
public static class WarLoadChecks
{
    public static readonly (int Year, int Day)[] RoundTrips = [(6, 20), (8, 40), (9, 10)];

    public static GateResult Run(Func<(WarLoadRun Run, WarLoadSoak Soak)> make, Action<string> log)
    {
        var problems = new List<string>(); var sb = new StringBuilder();
        string h1 = "", h2 = "";
        var (r1, s1) = make();
        using (r1)
        {
            s1.AtDayStart = (y, d, run) =>
            {
                if (RoundTrips.Contains((y, d))) { string msg = RoundTrip(run, y, d, problems); log(msg); sb.Append(msg).Append("; "); }
                if (y == 10 && d == 60) h1 = run.SaveHash();
            };
            s1.Run();
        }
        var (r2, s2) = make();
        using (r2) { s2.AtDayStart = (y, d, run) => { if (y == 10 && d == 60) h2 = run.SaveHash(); }; s2.Run(); }
        if (h1 == "" || h1 != h2) problems.Add($"hash at Y10 day 60 differs: {h1} against {h2}");
        sb.Append($"two runs at Y10 day 60: {h1} and {h2}");
        return new GateResult("WL-13", problems.Count == 0 ? AuditState.Pass : AuditState.Fail, sb + (problems.Count == 0 ? "" : " | PROBLEMS: " + string.Join("; ", problems)));
    }

    private static string RoundTrip(WarLoadRun run, int y, int d, List<string> problems)
    {
        var file = WorldSave.Capture(run.State, "h", "0.1");
        var bytes = SaveCodec.Encode(file);
        var st = WorldSave.Restore(SaveCodec.Decode(bytes), run.State.Content!, "h", null, run.State.Continent, run.State.History);
        st.Content = run.State.Content;
        using var rt = new WorldRuntime(st, st.Content!, new MemoryRegionPager());
        WarLoadRun.AttachLinks(rt);
        var again = WorldSave.Capture(st, "h", "0.1");
        var diff = new List<string>();
        foreach (var k in file.Sections.Keys.Union(again.Sections.Keys).Order(StringComparer.Ordinal))
            if (!(file.Sections.TryGetValue(k, out var a) && again.Sections.TryGetValue(k, out var b) && a.Span.SequenceEqual(b.Span))) diff.Add(k);
        bool warsupply = file.Sections.ContainsKey("warsupply");
        if (diff.Count > 0) problems.Add($"Y{y} day {d}: sections differ after load and save: {string.Join(", ", diff)}");
        return $"Y{y} day {d}: {file.Sections.Count} sections, {bytes.Length / 1024:N0} KiB, warsupply section {(warsupply ? "present" : "absent")}, {(diff.Count == 0 ? "byte-identical" : "DIFFERS in " + string.Join(",", diff))}";
    }
}
