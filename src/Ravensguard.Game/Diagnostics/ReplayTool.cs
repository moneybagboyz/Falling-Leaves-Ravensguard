using System;
using System.IO;
using System.Linq;
using Godot;
using Ravensguard.Game.UI.NewGame;
using Ravensguard.Game.World;
using Ravensguard.Sim.Codex;
using Ravensguard.Sim.Diagnostics;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Worlds;

namespace Ravensguard.Game.Diagnostics;

/// <summary>
/// Dev command <c>-- --replay bug.zip [--until tick] [--no-verify]</c> (testers spec 4.5): loads the autosave of the report (T0) into a headless live world, applies the
/// recorded commands at their ticks, and stops at the crash tick, at the first exception (printing its stack), or at the first record hash that differs.
/// A report from another build or content hash is replayed with a warning and the result is labelled unreliable. Exit code 0 = matched, 1 = failed, 2 = diverged.
/// </summary>
public static class ReplayTool
{
    public static int Run(string zipPath, string[] args)
    {
        try
        {
            var archive = ReportArchive.Open(zipPath);
            GD.Print($"replay: {Path.GetFileName(zipPath)} kind={archive.Kind} build={archive.Build.Version} git={archive.Build.Git}");
            if (archive.Trace == null) { GD.PrintErr("ERROR: replay: the report has no replay.trace (it was made without a running street view)"); return 1; }
            if (archive.Autosave == null) { GD.PrintErr("ERROR: replay: the report has no autosave.bin to start from (the trace needs it as T0)"); return 1; }
            var log = CommandLog.FromBytes(archive.Trace);
            var expected = archive.HashesCsv != null ? StateHashTrail.FromCsv(archive.HashesCsv) : null;
            var content = NewGameServices.Content();
            bool reliable = true;
            if (archive.MismatchWarning(new BuildInfo { Git = GameDiagnostics.BuildInfoNow().Git, ContentHash = content.Hash }) is { } warn) { GD.Print("WARNING: " + warn); reliable = false; }

            long? until = null; int u = Array.IndexOf(args, "--until");
            if (u >= 0 && u + 1 < args.Length && long.TryParse(args[u + 1], out var ut)) until = ut;

            string tmp = Path.Combine(Path.GetTempPath(), "ravensguard-replay-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tmp);
            try
            {
                string sav = Path.Combine(tmp, "t0.sav");
                File.WriteAllBytes(sav, archive.Autosave);
                var file = new SaveReader().Load(sav);
                var state = WorldSave.Restore(file, content.Db, content.Hash);
                state.Content = content.Db;
                using var runtime = new WorldRuntime(state, content.Db, StreetSource.MakePager());
                var region = state.Population.Region;
                var codex = CodexBook.Build(state.History, region, Ravensguard.Game.UI.CodexRules.FromInput());
                var source = StreetSource.ForRegion(region, state.Population, content.Db, state.Chunks, hero: state.Hero, codex: codex, history: state.History,
                    continent: state.Continent, resume: state.Live, regionSettings: state.RegionSettings, runtime: runtime);
                var world = source.CreateWorld(null, state.Clock.Now);
                world.SynchronousStreaming = true; // the window swap lands on the same tick every run
                GD.Print($"replay: T0 tick {log.BaseTick}, running to tick {until ?? log.EndTick} ({log.Items.Count} commands, {expected?.Count ?? 0} record hashes)");
                var res = ReplayRunner.Run(world, log, expected, new ReplayOptions { UntilTick = until });
                GD.Print("replay: " + res.Describe());
                if (!reliable) GD.Print("replay: result is UNRELIABLE (build or content differs)");
                if (archive.CrashText != null) GD.Print("replay: the report's crash.txt begins: " + archive.CrashText.Split('\n').FirstOrDefault());
                return res.Exception != null ? (archive.Kind == "crash" ? 0 : 1) : res.Divergence != null ? 2 : 0;
            }
            finally { try { Directory.Delete(tmp, true); } catch (IOException) { } }
        }
        catch (Exception ex)
        {
            GD.PrintErr("ERROR: replay failed: " + ex.Message);
            return 1;
        }
    }
}
