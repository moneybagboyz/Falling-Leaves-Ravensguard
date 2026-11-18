using System;
using System.Linq;
using Godot;
using Ravensguard.Game.UI.NewGame;
using Ravensguard.Sim.Onboarding;

namespace Ravensguard.Game.Onboarding;

/// <summary>
/// <c>--autoplay</c> (onboarding spec 7, plan 6): runs the first-hour bot for every origin over fresh worlds and prints the timelines. Exit code 0 when every first ride and first odd job completed, 1 otherwise.
/// <c>--autoplay-seeds n</c> plays n seeds (1234, then 1, 2, ...; default 1), <c>--autoplay-origin id</c> one origin only.
/// </summary>
public static class AutoplayTool
{
    public static int Run(string[] args)
    {
        string? Val(string n) { int i = Array.IndexOf(args, n); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }
        int n = int.TryParse(Val("--autoplay-seeds"), out var k) ? Math.Clamp(k, 1, 6) : 1;
        ulong[] all = [1234, 1, 2, 3, 4, 5];
        var content = NewGameServices.Content();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var reports = AutoplayRunner.Run(content.Db, content.Dir, all.Take(n), Val("--autoplay-origin"), GD.Print);
        GD.Print($"[autoplay] {reports.Count} first hours in {sw.Elapsed.TotalSeconds:0.0} s: {(reports.All(r => r.Passed) ? "ok" : "FAILED")}");
        foreach (var r in reports.Where(r => !r.Passed)) GD.PrintErr($"ERROR: autoplay {r.Origin} seed {r.Seed}: {r.Failure}");
        return reports.All(r => r.Passed) && reports.Count > 0 ? 0 : 1;
    }
}
