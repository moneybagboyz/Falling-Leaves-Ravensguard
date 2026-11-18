using System;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Ravensguard.Sim.WorldGen;
using Ravensguard.Sim.WorldGen.History;
using Ravensguard.Sim.Worlds;

namespace Ravensguard.Game.UI.NewGame;

/// <summary>Smoke test and screenshot driver for the new-game screens (dev flags only; never used in play).</summary>
public static class NewGameDebug
{
    static async Task Frames(SceneTree t, int n) { for (int i = 0; i < n; i++) await t.ToSignal(t, SceneTree.SignalName.ProcessFrame); }

    static async Task<bool> Until(SceneTree t, Func<bool> done, int maxFrames = 3000)
    {
        for (int i = 0; i < maxFrames && !done(); i++) await Frames(t, 1);
        return done();
    }

    /// <summary>Makes a small world in the (temporary) library synchronously and returns it.</summary>
    static LoadedWorld MakeWorld(int years, ulong seed)
    {
        var content = NewGameServices.Content();
        var cs = ContinentSettings.LoadDefaults(content.Dir); cs.Seed = seed;
        var hs = HistorySettings.LoadDefaults(content.Dir); hs.Seed = seed; hs.Years = years;
        var w = NewGameServices.Library.Create(WorldLibrary.DefaultName(seed), cs, hs, content.Hash, "smoke");
        NewGameServices.Remember(w);
        return w;
    }

    public static async Task Smoke(SceneTree tree, ScreenRouter router)
    {
        var tmp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "rg-smoke-" + Guid.NewGuid().ToString("N"));
        NewGameServices.Configure(["--ng-library", tmp]);
        try
        {
            router.Push(new QuickStartScreen(router)); await Frames(tree, 2); router.Pop(); await Frames(tree, 1);
            router.Push(new WorldsScreen(router, false)); await Frames(tree, 2); router.Pop(); await Frames(tree, 1);

            // creation through the worker job, then the chronicle viewer and the embark map
            var d = NewGameServices.NewDraft(); d.History.Years = 40; d.ChooseRegion = false;
            var creation = new WorldCreationScreen(router, d, WorldPurpose.Legends);
            router.Push(creation);
            if (!await Until(tree, () => router.Top is LegendsScreen)) throw new Exception("world creation did not finish");
            await Frames(tree, 2);
            var w = NewGameServices.Library.List().First();
            router.Pop(); await Frames(tree, 1);
            router.Push(new WorldsScreen(router, true)); await Frames(tree, 2); router.Pop(); await Frames(tree, 1);
            var loaded = NewGameServices.Cached(w.Id) ?? throw new Exception("world not cached");
            var embark = new EmbarkMapScreen(router, d, loaded);
            router.Push(embark); await Frames(tree, 3);
            await Task.Delay(1500); await Frames(tree, 3);
            router.Pop(); await Frames(tree, 1);
            // opening a library world by id runs the same screen through the load path
            router.Push(new WorldCreationScreen(router, w, WorldPurpose.Legends));
            if (!await Until(tree, () => router.Top is LegendsScreen)) throw new Exception("library load did not finish");
            router.Pop(); await Frames(tree, 1);
            // the region build ends in the street view
            var d2 = NewGameServices.NewDraft(); d2.Name = "Smoke Tester"; d2.LibraryWorldId = w.Id;
            router.Push(new RegionBuildScreen(router, d2, loaded));
            if (!await Until(tree, () => router.Top is Ravensguard.Game.World.StreetView, 6000)) throw new Exception("region build did not finish");
            // the generated region is played in window-local cells: a click must start the hero walking (it did not once)
            var sv = (Ravensguard.Game.World.StreetView)router.Top!;
            await Frames(tree, 5);
            // the world is running: the new game has its economy, and the tick worker catches up the opening hours and then steps three more game hours on its own
            var rt = sv.Runtime ?? throw new Exception("the street view has no world runtime");
            if (rt.State.Production == null || rt.State.Markets == null || rt.State.Farming == null) throw new Exception("a new game has no economy");
            if (!await Until(tree, () => rt.HourDone >= sv.DebugWorld.Clock.Now.TotalHours, 3000)) throw new Exception($"the opening hours were not stepped (hour {rt.HourDone}, clock {sv.DebugWorld.Clock.Now.TotalHours}, fault {rt.Fault})");
            long hour0 = rt.HourDone;
            sv.DebugWorld.SetSpeed(Ravensguard.Sim.Core.SimSpeed.X8);
            sv.DebugWorld.Clock.Advance(3 * 3600 / (Ravensguard.Sim.Core.TimeConst.GameSecondsPerRealSecond * 8.0) + 0.01); // three game hours at 8x
            if (!await Until(tree, () => rt.HourDone >= hour0 + 3, 3000)) throw new Exception($"the tick did not step three game hours (hour {rt.HourDone}, wanted {hour0 + 3}, fault {rt.Fault})");
            if (rt.Fault != null) throw new Exception("the world tick faulted: " + rt.Fault);
            sv.DebugWorld.SetSpeed(Ravensguard.Sim.Core.SimSpeed.X1);
            var tick = rt.Snapshot;
            if (tick.HoursStepped < 3 || tick.HomeAlive <= 0 || tick.Resident < 1) throw new Exception($"the barrier snapshot is empty (hours {tick.HoursStepped}, alive {tick.HomeAlive}, regions {tick.Resident})");
            GD.Print($"[smoke] world tick: {tick.HoursStepped} hours, worker {tick.HourMs:0} ms/hour (max {tick.MaxHourMs:0}), {tick.Resident} regions, home alive {tick.HomeAlive}; main thread pump {rt.PumpMsTotal / Math.Max(1, rt.Pumps) * 1000:0.0} us avg, {rt.MaxPumpMs:0.00} ms max");
            var click = sv.DebugClickAround();
            if (!click.Accepted || click.Moved <= 0) throw new Exception($"click-to-move in region mode did not move the hero (accepted {click.Accepted}, cell {click.Clicked}, moved {click.Moved})");
            // Esc opens the pause menu and leaves the street view on top; Esc again closes it; the router must not pop the run
            int depth = router.Depth;
            if (!sv.DebugEsc()) throw new Exception("Esc did not open the pause menu over the street view");
            await Frames(tree, 2);
            var esc = new InputEventKey { Keycode = Key.Escape, PhysicalKeycode = Key.Escape, Pressed = true };
            Input.ParseInputEvent(esc);
            await Frames(tree, 3);
            if (router.Top != sv || router.Depth != depth || router.HasModal) throw new Exception("Esc in the pause menu did not just close it (depth " + router.Depth + ", modal " + router.HasModal + ")");
            // save, leave, Continue: the hero stands where it was, with the clock, torch, Codex and explored map (plan 2.11)
            var run = sv.DebugWorld;
            run.SetTorch(true);
            var book = run.Codex ?? throw new Exception("the live world has no Codex attached");
            var fresh = book.Entries.FirstOrDefault(e => !e.Unlocked) ?? throw new Exception("every Codex entry is already open");
            book.Unlock(fresh.Id);
            run.VisitNear(run.WorldCell.X, run.WorldCell.Y, 2000);
            var before = run.CaptureResume();
            if (before.Codex == null || !before.Codex.Unlocked.Contains(fresh.Id)) throw new Exception("the unlocked Codex entry is not in the live capture");
            if (!sv.StartSave(SaveKind.Manual)) throw new Exception("the street view would not save its run");
            if (!await Until(tree, () => !sv.Saving, 1500)) throw new Exception("the save did not finish");
            if (sv.LastSaveError != null) throw new Exception("save failed: " + sv.LastSaveError);
            var runsDir = NewGameServices.Library.RunsDirectory(w.Id);
            if (System.IO.Directory.GetFiles(runsDir, "*.sav").Length == 0) throw new Exception("no save file was written");
            sv.DebugLeave(); await Frames(tree, 3);
            if (router.Top is not MainMenu menu) throw new Exception("leaving the run did not reach the main menu");
            router.Push(new RunsScreen(router)); await Frames(tree, 2); router.Pop(); await Frames(tree, 1); // Load: all worlds
            router.Push(new RunsScreen(router, w.Id)); await Frames(tree, 2); router.Pop(); await Frames(tree, 1); // Load: one world
            await Frames(tree, 1);
            Input.ParseInputEvent(new InputEventKey { Keycode = Key.K, PhysicalKeycode = Key.K, Pressed = true }); // Continue
            if (!await Until(tree, () => router.Top is Ravensguard.Game.World.StreetView, 6000)) throw new Exception("Continue did not reach the street view" + (router.Top is RunLoadScreen ? " (still loading)" : " (top is " + router.Top?.GetType().Name + ")"));
            sv = (Ravensguard.Game.World.StreetView)router.Top!;
            await Frames(tree, 2);
            var back = sv.DebugWorld.CaptureResume();
            if (back.WorldXRaw != before.WorldXRaw || back.WorldYRaw != before.WorldYRaw || back.Z != before.Z || back.Facing != before.Facing)
                throw new Exception($"the hero came back at ({back.WorldXRaw},{back.WorldYRaw},z{back.Z}) instead of ({before.WorldXRaw},{before.WorldYRaw},z{before.Z})");
            if (back.Torch != before.Torch) throw new Exception("the torch state was not restored");
            long dt = back.Time.Seconds - before.Time.Seconds;
            if (dt < 0 || dt > 3600) throw new Exception($"the clock came back {dt} s off");
            if (back.Codex == null || !back.Codex.Unlocked.SetEquals(before.Codex.Unlocked) || sv.DebugWorld.Codex?.Get(fresh.Id)?.Unlocked != true) throw new Exception("the Codex progress was not restored");
            if (!back.Explored.SameAs(before.Explored) || back.Explored.Count == 0) throw new Exception("the explored map was not restored");
            if (sv.Run == null) throw new Exception("the resumed view has no run to save");
            // autosave: the timer writes the first rotating slot, then goes quiet
            sv.AutosaveSeconds = 0.05;
            string autosave = Ravensguard.Sim.Save.SaveWriter.AutosavePath(runsDir, 1, sv.Run.RunName);
            if (!await Until(tree, () => System.IO.File.Exists(autosave) && !sv.Saving, 1500)) throw new Exception("no autosave was written");
            sv.AutosaveSeconds = 900;
            if (sv.LastSaveError != null) throw new Exception("autosave failed: " + sv.LastSaveError);
            // F5: quicksave writes the run's manual slot again
            var stamp = System.IO.File.GetLastWriteTimeUtc(sv.Run.LastPath!);
            await Task.Delay(30);
            Input.ParseInputEvent(new InputEventKey { Keycode = Key.F5, PhysicalKeycode = Key.F5, Pressed = true });
            if (!await Until(tree, () => sv.Saving, 60)) throw new Exception("F5 did not start a save");
            if (!await Until(tree, () => !sv.Saving, 1500) || System.IO.File.GetLastWriteTimeUtc(sv.Run.LastPath!) <= stamp) throw new Exception("quicksave did not rewrite the manual slot");
            // F9: quickload brings the run back from its manual slot (a new view, a new world runtime), the abstract hour where the save left it
            var before9 = sv;
            Input.ParseInputEvent(new InputEventKey { Keycode = Key.F9, PhysicalKeycode = Key.F9, Pressed = true });
            if (!await Until(tree, () => router.Top is Ravensguard.Game.World.StreetView v && v != before9, 6000)) throw new Exception("F9 did not load the quicksave" + (router.Top is RunLoadScreen ? " (still loading)" : " (top is " + router.Top?.GetType().Name + ")"));
            sv = (Ravensguard.Game.World.StreetView)router.Top!;
            await Frames(tree, 3);
            if (sv.Runtime == null || sv.Run == null || sv.Run.RunName != before9.Run!.RunName) throw new Exception("the quickloaded run lost its runtime or its name");
            // quit to menu without saving returns to the main menu and clears the hand-off
            sv.DebugEsc(); await Frames(tree, 2);
            router.CloseModal(); await Frames(tree, 1);
            router.ReturnToMainMenu(); await Frames(tree, 2);
            if (router.Top is not MainMenu) throw new Exception("ReturnToMainMenu did not reach the main menu");
            if (RegionBuildScreen.Latest != null) throw new Exception("RegionBuildScreen.Latest not cleared when the street view exited");
        }
        finally { try { System.IO.Directory.Delete(tmp, true); } catch { } }
    }

    /// <summary>--ng-shot quickstart|creation|embark|legends --shot-out file.png [--ng-layer N] [--ng-years N] [--ng-hover x,y] [--ng-delay-ms N]</summary>
    public static async Task Shot(SceneTree tree, ScreenRouter router, string[] args)
    {
        string Arg(string name, string def) { int i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length ? args[i + 1] : def; }
        string what = Arg("--ng-shot", "quickstart"), outPath = Arg("--shot-out", "/tmp/ng.png");
        int years = int.Parse(Arg("--ng-years", "120"));
        var tmp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "rg-shot-" + Guid.NewGuid().ToString("N"));
        if (Array.IndexOf(args, "--ng-library") < 0) NewGameServices.Configure(["--ng-library", tmp]);
        await Frames(tree, 3);
        switch (what)
        {
            case "quickstart":
                {
                    var q = new QuickStartScreen(router); router.Push(q);
                    await Frames(tree, 4);
                    if (Arg("--ng-advanced", "0") == "1") { q.DebugAdvanced(); await Frames(tree, 3); }
                    break;
                }
            case "creation":
                {
                    var d = NewGameServices.NewDraft(); d.History.Years = years; d.Name = "Shot";
                    router.Push(new WorldCreationScreen(router, d, WorldPurpose.Legends));
                    await Task.Delay(int.Parse(Arg("--ng-delay-ms", "6000")));
                    await Frames(tree, 3);
                    break;
                }
            case "embark":
            case "legends":
                {
                    var w = MakeWorld(years, 7);
                    var d = NewGameServices.NewDraft();
                    if (what == "embark")
                    {
                        var e = new EmbarkMapScreen(router, d, w); router.Push(e);
                        await Frames(tree, 3); await Task.Delay(2500); await Frames(tree, 3);
                        e.DebugSetLayer(int.Parse(Arg("--ng-layer", "2")));
                        var hv = Arg("--ng-hover", "").Split(',');
                        if (hv.Length == 2) e.DebugHover(new Vector2I(int.Parse(hv[0]), int.Parse(hv[1])));
                    }
                    else
                    {
                        var l = new LegendsScreen(router, w); router.Push(l);
                        await Frames(tree, 3);
                        l.DebugFollow();
                    }
                    await Frames(tree, 4);
                    break;
                }
        }
        var img = tree.Root.GetViewport().GetTexture().GetImage();
        img.SavePng(outPath);
        GD.Print("ng-shot: wrote " + outPath);
        try { System.IO.Directory.Delete(tmp, true); } catch { }
        tree.Quit(0);
    }
}
