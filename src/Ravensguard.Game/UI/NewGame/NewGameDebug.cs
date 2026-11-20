using System;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Ravensguard.Sim.Construction;
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

    /// <summary>The smoke's world seed. It was random (a fresh seed per run), so CI met worlds where nobody stood near the hero and no bark came.</summary>
    public const ulong SmokeSeed = 20261119;

    public static async Task Smoke(SceneTree tree, ScreenRouter router)
    {
        var tmp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "rg-smoke-" + Guid.NewGuid().ToString("N"));
        NewGameServices.Configure(["--ng-library", tmp]);
        try
        {
            router.Push(new QuickStartScreen(router)); await Frames(tree, 2); router.Pop(); await Frames(tree, 1);
            router.Push(new WorldsScreen(router, false)); await Frames(tree, 2); router.Pop(); await Frames(tree, 1);

            // creation through the worker job, then the chronicle viewer and the embark map
            var d = NewGameServices.NewDraft(); NewGameServices.Reseed(d, SmokeSeed); d.History.Years = 40; d.ChooseRegion = false;   // one fixed world: a smoke that fails must fail the same way on every machine
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
            // every Phase 3 screen at 960x540 through the real flow: fits, Esc returns; barks; encounter, trade, back
            await Ravensguard.Game.Debug.Phase3Smoke.Run(tree, router, sv);
            // save, leave, Continue: the hero stands where it was, with the clock, torch, Codex and explored map (plan 2.11)
            var run = sv.DebugWorld;
            run.SetTorch(true);
            var book = run.Codex ?? throw new Exception("the live world has no Codex attached");
            var fresh = book.Entries.FirstOrDefault(e => !e.Unlocked) ?? throw new Exception("every Codex entry is already open");
            book.Unlock(fresh.Id);
            run.VisitNear(run.WorldCell.X, run.WorldCell.Y, 2000);
            run.ClearPath(); // a walk in progress is saved now and goes on after the load (Phase 3 cleanup): stand still, so "where it was" is one spot
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
            // testers: walk, write a bug report (as F8 does) and replay it headlessly: the replayed record hashes must equal the recorded ones (spec 4.5, TR-10)
            {
                var rec = Ravensguard.Game.Diagnostics.GameDiagnostics.Recorder ?? throw new Exception("the street view has no session recorder");
                if (!await Until(tree, () => Ravensguard.Game.Diagnostics.GameDiagnostics.ReplayReady && !sv.Saving, 3000)) throw new Exception("the autosave copy for replay was not kept");
                long t0 = rec.Commands.BaseTick;
                rec.SetInput(new Ravensguard.Sim.Live.MoveInput(1, 0));
                if (!await Until(tree, () => rec.World.Tick >= t0 + 120, 8000)) throw new Exception("the live world did not step");
                rec.SetInput(new Ravensguard.Sim.Live.MoveInput(0, 1, true));
                if (!await Until(tree, () => rec.World.Tick >= t0 + 260, 8000)) throw new Exception("the live world did not step");
                rec.SetInput(Ravensguard.Sim.Live.MoveInput.None);
                var rtmp = System.IO.Path.Combine(tmp, "report");
                var consent = new Ravensguard.Sim.Diagnostics.ConsentStore(System.IO.Path.Combine(rtmp, "profile"), System.IO.Path.Combine(rtmp, "reports"));
                if (consent.IsGranted) throw new Exception("consent is on by default");
                consent.Accept();
                var request = Ravensguard.Game.Diagnostics.GameDiagnostics.BuildRequest(Ravensguard.Sim.Diagnostics.ReportKind.Bug, null, null, "Bug", "smoke", "", "yes", true);
                var report = new Ravensguard.Sim.Diagnostics.ReportBuilder(consent, Ravensguard.Game.Diagnostics.GameDiagnostics.Redactor, System.IO.Path.Combine(rtmp, "reports")).Write(request);
                foreach (var need in new[] { "autosave.bin", "replay.trace", "hashes.csv", "session.json", "system.json", "content_manifest.json", "log_tail.txt" })
                    if (!report.Files.Any(f => f.Name == need)) throw new Exception("the report has no " + need);
                int code = Ravensguard.Game.Diagnostics.ReplayTool.Run(report.Path, []);
                if (code != 0) throw new Exception("replaying the bug report did not reproduce the recorded hashes (exit " + code + ")");
            }
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

    /// <summary>
    /// P5-C screenshots of build mode (--ng-shot build --ng-scene place | library | fitted | saveas | site): the hero buys the empty plot nearest him (a dev faucet pays for it; the purchase
    /// goes through his desk) and walks to it; then free placement with a wall dragged out past the plot's edge (the cells off his land refused, the reason beside the cursor), the plan library
    /// on that plot, a plan fitted to the plot and placed as ghosts, save as blueprint's rectangle and name over them, and the site panel with a hired crew at work.
    /// </summary>
    static async Task BuildModeShot(SceneTree tree, Ravensguard.Game.World.StreetView sv, string scene)
    {
        var clock = sv.DebugWorld.Clock;
        var lot = await sv.DebugBuyLot() ?? throw new Exception("build shot: no empty plot for sale near the hero");
        GD.Print($"[build] bought the plot {lot}");
        int cx = lot.X + lot.W / 2, cy = lot.Y + lot.H / 2;
        // set the hero down at the plot's street edge (dev), then let the desk's hour come round for the purchase
        var (fx, fy) = lot.Street switch { 0 => (cx, lot.Y - 2), 1 => (lot.Right + 1, cy), 2 => (cx, lot.Bottom + 1), 3 => (lot.X - 2, cy), _ => (cx, cy) };
        sv.DebugPlaceHero(fx, fy);
        sv.DebugWorld.SetSpeed(Ravensguard.Sim.Core.SimSpeed.X8);
        bool may = false;
        for (int i = 0; i < 600 && !may; i++) { await Frames(tree, 5); may = await sv.DebugMayBuild(cx, cy); }
        sv.DebugWorld.SetSpeed(Ravensguard.Sim.Core.SimSpeed.Paused);
        if (!may) throw new Exception("build shot: the plot was not bought");
        GD.Print($"[build] the plot is the hero's at {clock.Now}; the hero at {sv.DebugHeroCell}");
        await Frames(tree, 60);
        if (!sv.DebugBuildMode(true)) throw new Exception("build shot: build mode did not open");
        sv.DebugPanTo(cx, cy + 4);                                               // the plot in the upper part of the screen, clear of the palette
        await Frames(tree, 10);
        string? plan = null;
        var lib = Ravensguard.Sim.Construction.PlanLibrary.For(NewGameServices.Content().Db);
        foreach (var id in new[] { "burgage_middling", "burgage_poor", "cottage_two_room", "peasant_cottage", "smithy_house", "test_cottage" })
            if (lib.Grid(id) is { } g && Ravensguard.Sim.Construction.PlanFitter.Fit(g, lot, lib.Library.Pieces).Ok) { plan = id; break; }
        switch (scene)
        {
            case "place":
            {
                // a wattle wall dragged from inside the plot out across its side edge: the cells off the hero's land are refused, with the holder's name
                // a wattle wall dragged from inside the plot out across its north edge (the west edge for a plot facing north): the cells off the hero's land are refused
                sv.DebugBuildPiece("wall_wattle");
                var from = lot.Street == 0 ? (lot.X + 4, lot.Y + 3) : (cx + 5, lot.Y + 3);
                var to = lot.Street == 0 ? (lot.X - 4, lot.Y + 3) : (cx + 5, lot.Y - 4);
                sv.DebugPanTo(Math.Min(from.Item1, to.Item1) - 4, Math.Min(from.Item2, to.Item2) + 7);
                sv.DebugBuildCursor(to.Item1, to.Item2, from);
                await Until(tree, () => sv.DebugBuildAnswer is { } a && a.Cells.Count > 2, 600);
                await Frames(tree, 20);
                break;
            }
            case "library":
                sv.DebugBuildCursor(cx, cy);
                await Until(tree, () => sv.DebugBuildAnswer is { Lot: not null }, 600);
                if (!sv.DebugOpenPlanLibrary()) throw new Exception("build shot: the plan library did not open");
                await Frames(tree, 30);
                break;
            default:
            {
                if (plan == null) throw new Exception("build shot: no plan fits the plot " + lot);
                // the first plan and turn whose every cell may go here (a plot can hold a yard's fences or paving: the preview refuses those cells)
                string? best = null; int bestIdx = -1, bestRefused = int.MaxValue;
                foreach (var id in new[] { plan, "burgage_poor", "peasant_cottage", "test_cottage", "cottage_two_room", "smithy_house" })
                {
                    for (int idx = -1; idx < 8 && bestRefused > 0; idx++)
                    {
                        sv.DebugPlan(id, idx);
                        sv.DebugBuildCursor(cx, cy);
                        var want = PlanFitter.Transform(Math.Max(0, idx));
                        await Until(tree, () => sv.DebugBuildAnswer is { Ask.Plan: true } a && a.Ask.PlanId == id && a.Ask.Auto == (idx < 0) && (idx < 0 || (a.Ask.Turns == want.Turns && a.Ask.Mirror == want.Mirror)), 600);
                        var ans = sv.DebugBuildAnswer;
                        if (ans?.Fit is { Ok: true } && ans.Preview.Refused.Count < bestRefused) { best = id; bestIdx = idx; bestRefused = ans.Preview.Refused.Count; }
                    }
                    if (bestRefused == 0) break;
                }
                if (best == null) throw new Exception("build shot: no plan fits the plot " + lot);
                sv.DebugPlan(best, bestIdx);
                sv.DebugBuildCursor(cx, cy);
                await Until(tree, () => sv.DebugBuildAnswer is { Ask.Plan: true, Fit.Ok: true } a && a.Ask.PlanId == best, 600);
                await Frames(tree, 10);
                GD.Print($"[build] {best} fitted: {sv.DebugBuildAnswer?.Fit}; refused: " + string.Join(", ", (sv.DebugBuildAnswer?.Preview.Refused ?? []).Take(6).Select(r => $"{r.Piece}@{r.X},{r.Y},{r.Z}:{r.Key}")));
                sv.DebugPlanPlace();
                sv.DebugWorld.SetSpeed(Ravensguard.Sim.Core.SimSpeed.X8);                 // the desk applies the command at its hour's barrier
                bool placed = await Until(tree, () => sv.DebugGhostCount > 0, 6000);
                sv.DebugWorld.SetSpeed(Ravensguard.Sim.Core.SimSpeed.Paused);
                if (!placed) throw new Exception("build shot: the plan was not placed");
                if (scene == "fitted") { sv.DebugBuildCursor(cx, cy); await Frames(tree, 30); break; }
                if (scene == "saveas")
                {
                    var d = sv.DebugSaveRect(lot.X, lot.Y, lot.Right - 1, lot.Bottom - 1);
                    d?.DebugName("My burgage");
                    await Frames(tree, 20);
                    break;
                }
                // site: hands hired and the pile stocked (the dev faucet), the clock run until they work
                sv.DebugBuildMode(false);
                GD.Print("[build] " + sv.DebugDevBuild("stock"));
                GD.Print("[build] " + sv.DebugDevBuild("finish", "25"));
                GD.Print("[build] " + sv.DebugDevBuild("crew", "3"));
                sv.DebugWorld.SetSpeed(Ravensguard.Sim.Core.SimSpeed.X8);
                long from = clock.Now.Seconds;
                await Until(tree, () => clock.Now.Seconds >= from + 240, 20000);
                sv.DebugWorld.SetSpeed(Ravensguard.Sim.Core.SimSpeed.Paused);
                sv.DebugBuildMode(true);
                if (!sv.DebugOpenSitePanel()) throw new Exception("build shot: the site panel did not open");
                await Frames(tree, 40);
                break;
            }
        }
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
                    if (Arg("--ng-origin", "").Length > 0) { q.DebugSelect(Arg("--ng-origin", "")); await Frames(tree, 3); }   // P5-H: the widow's card picked
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
            case "crafting":
                {
                    // dev: the crafting screen for a made-up hero with a starter pack, a hearth and a workbench in reach (--ng-craft-recipe <id> selects a recipe)
                    var db = NewGameServices.Content().Db;
                    var origin = NewGameServices.Origins()[0];
                    var hero = Ravensguard.Sim.Characters.HeroRecord.FromOrigin("Ada", true, origin);
                    // P5-E: --ng-craft-scene smithy | commission | kit (the station panel from a detected smithy, the Commission tab, a field-kit repair)
                    string scene = Arg("--ng-craft-scene", "");
                    if (scene.Length > 0)
                    {
                        var (ss, recipe, commission) = CraftShotScenes.Make(db, hero, scene);
                        router.Push(new CraftingScreen(router, ss) { StartRecipe = Arg("--ng-craft-recipe", recipe), StartCommission = commission });
                        await Frames(tree, 10);
                        break;
                    }
                    var session = new CraftingSession(db, hero, 8 * 3600, true, ["hearth", "oven", "workbench"], 255);
                    session.State.Book.Set(CraftingSession.HeroId, Ravensguard.Sim.People.Skill.Baking, 45_000_000);
                    session.State.Book.Set(CraftingSession.HeroId, Ravensguard.Sim.People.Skill.Tailoring, 12_000_000);
                    session.State.Book.Set(CraftingSession.HeroId, Ravensguard.Sim.People.Skill.Cooking, 18_000_000);
                    router.Push(new CraftingScreen(router, session) { StartRecipe = Arg("--ng-craft-recipe", "bake_loaf") });
                    await Frames(tree, 10);
                    break;
                }
            case "onboarding":
                {
                    // dev: a real new game for an origin (--ng-origin id), then a staged scene of the first hour: mentor | hint | overlay | overlay_mounted | yard | journal
                    var w = MakeWorld(Math.Min(years, 60), 7);
                    var d = NewGameServices.NewDraft(); d.Name = "Shot"; d.LibraryWorldId = w.Info.Id;
                    d.OriginId = Arg("--ng-origin", d.OriginId);
                    var loaded = w;
                    router.Push(new RegionBuildScreen(router, d, loaded));
                    if (!await Until(tree, () => router.Top is Ravensguard.Game.World.StreetView, 9000)) throw new Exception("onboarding shot: the region build did not finish");
                    var sv = (Ravensguard.Game.World.StreetView)router.Top!; var rt = sv.Runtime!;
                    await Until(tree, () => rt.HourDone >= sv.DebugWorld.Clock.Now.TotalHours, 6000);
                    await Frames(tree, 240);
                    GD.Print("onboarding shot: " + sv.DebugMentorLine());
                    string scene = Arg("--ng-scene", "hint");
                    if (scene == "hint") { sv.DebugOnboarding("hint"); await Frames(tree, 40); }
                    else { sv.DebugOnboarding(scene); await Frames(tree, 40); }
                    break;
                }
            case "build":
                {
                    // dev (P5-B): a real new game, then a staged building site beside the hero: --ng-scene half | scaffold | collapse
                    var w = MakeWorld(Math.Min(years, 60), 7);
                    var d = NewGameServices.NewDraft(); d.Name = "Shot"; d.LibraryWorldId = w.Info.Id;
                    router.Push(new RegionBuildScreen(router, d, w));
                    if (!await Until(tree, () => router.Top is Ravensguard.Game.World.StreetView, 9000)) throw new Exception("build shot: the region build did not finish");
                    var sv = (Ravensguard.Game.World.StreetView)router.Top!; var rt = sv.Runtime!;
                    await Until(tree, () => rt.HourDone >= sv.DebugWorld.Clock.Now.TotalHours, 6000);
                    await Frames(tree, 60);
                    string scene = Arg("--ng-scene", "half");
                    if (scene is "place" or "library" or "fitted" or "saveas" or "site") { await BuildModeShot(tree, sv, scene); break; }   // P5-C
                    sv.DebugBuild(scene == "fallen" ? "collapse" : scene);
                    // let the clock run until the crews are at work (a game minute or two), or the floor has fallen; a groan is caught at once, paused
                    var clock = sv.DebugWorld.Clock; long from = clock.Now.Seconds;
                    if (scene != "collapse")
                    {
                        sv.DebugWorld.SetSpeed(Ravensguard.Sim.Core.SimSpeed.X8);
                        await Until(tree, () => clock.Now.Seconds >= from + (scene == "fallen" ? 30 : 150), 20000);
                    }
                    sv.DebugWorld.SetSpeed(Ravensguard.Sim.Core.SimSpeed.Paused);
                    await Frames(tree, 30);
                    break;
                }
            case "fire":
                {
                    // dev (P6-B5): a real new game, then a row of thatched cottages beside the hero with the first alight, stepped on 120 sim-seconds
                    var w = MakeWorld(Math.Min(years, 60), 7);
                    var d = NewGameServices.NewDraft(); d.Name = "Shot"; d.LibraryWorldId = w.Info.Id;
                    router.Push(new RegionBuildScreen(router, d, w));
                    if (!await Until(tree, () => router.Top is Ravensguard.Game.World.StreetView, 9000)) throw new Exception("fire shot: the region build did not finish");
                    var sv = (Ravensguard.Game.World.StreetView)router.Top!; var rt = sv.Runtime!;
                    await Until(tree, () => rt.HourDone >= sv.DebugWorld.Clock.Now.TotalHours, 6000);
                    await Frames(tree, 60);
                    sv.DebugFire();
                    await Frames(tree, 60);
                    break;
                }
            case "homestead":
                {
                    // dev (P6-B6): a real new game, then a claim beside the hero with an acre felled, grubbed and a field of his household where the trees stood; the Q menu with the holdings verbs
                    var w = MakeWorld(Math.Min(years, 60), 7);
                    var d = NewGameServices.NewDraft(); d.Name = "Shot"; d.LibraryWorldId = w.Info.Id;
                    router.Push(new RegionBuildScreen(router, d, w));
                    if (!await Until(tree, () => router.Top is Ravensguard.Game.World.StreetView, 9000)) throw new Exception("homestead shot: the region build did not finish");
                    var sv = (Ravensguard.Game.World.StreetView)router.Top!; var rt = sv.Runtime!;
                    await Until(tree, () => rt.HourDone >= sv.DebugWorld.Clock.Now.TotalHours, 6000);
                    await Frames(tree, 60);
                    GD.Print("[homestead] " + sv.DebugHomestead());
                    await Frames(tree, 150);
                    // the Q menu first, scrolled to the holdings verbs, saved beside the shot; then the street itself with the acre where the trees stood
                    sv.DebugQMenu();
                    await Frames(tree, 20);
                    if (router.Top is QMenu qm)
                    {
                        var head = qm.FindChildren("*", "Label", true, false).OfType<Label>().FirstOrDefault(l => l.Text == L.T("hw_q_head"));
                        if (head != null && qm.FindChildren("*", "ScrollContainer", true, false).FirstOrDefault() is ScrollContainer sc) { await Frames(tree, 2); sc.ScrollVertical = Math.Max(0, sc.ScrollVertical + (int)(head.GlobalPosition.Y - sc.GlobalPosition.Y)); }
                    }
                    await Frames(tree, 20);
                    tree.Root.GetViewport().GetTexture().GetImage().SavePng(outPath.Replace(".png", "-menu.png"));
                    router.Pop();
                    await Frames(tree, 40);
                    break;
                }
            case "vehicles":
                {
                    // dev (P5-G): a real new game, then --ng-scene designer (the designer with a wagon) | wagon (a designed wagon driven east) | diagonal (driven south-east, the baked diagonal)
                    // | stuck (an ox wagon stuck on a mud road, the bar's warning) | site (an unfinished wagon beside the hero) | boat (a barge on the nearest water with the hero aboard)
                    var w = MakeWorld(Math.Min(years, 60), 7);
                    var d = NewGameServices.NewDraft(); d.Name = "Shot"; d.LibraryWorldId = w.Info.Id;
                    router.Push(new RegionBuildScreen(router, d, w));
                    if (!await Until(tree, () => router.Top is Ravensguard.Game.World.StreetView, 9000)) throw new Exception("vehicles shot: the region build did not finish");
                    var sv = (Ravensguard.Game.World.StreetView)router.Top!; var rt = sv.Runtime!;
                    await Until(tree, () => rt.HourDone >= sv.DebugWorld.Clock.Now.TotalHours, 6000);
                    await Frames(tree, 60);
                    string scene = Arg("--ng-scene", "designer");
                    if (scene == "boat") { GD.Print("[vehicles] " + sv.DebugVehicles("river")); await Frames(tree, 240); }    // the window streams the river in
                    GD.Print("[vehicles] " + sv.DebugVehicles(scene));
                    await Frames(tree, 40);
                    break;
                }
            case "contracts":
                {
                    // dev (P5-D): a real new game, then --ng-scene growth (a growth cottage 40% done beside the hero, its household at work) | builder (a builder hired for a cottage)
                    // | board (the contract board with the lord's posted work and builders' bids)
                    var w = MakeWorld(Math.Min(years, 60), 7);
                    var d = NewGameServices.NewDraft(); d.Name = "Shot"; d.LibraryWorldId = w.Info.Id;
                    router.Push(new RegionBuildScreen(router, d, w));
                    if (!await Until(tree, () => router.Top is Ravensguard.Game.World.StreetView, 9000)) throw new Exception("contracts shot: the region build did not finish");
                    var sv = (Ravensguard.Game.World.StreetView)router.Top!; var rt = sv.Runtime!;
                    await Until(tree, () => rt.HourDone >= sv.DebugWorld.Clock.Now.TotalHours, 6000);
                    await Frames(tree, 60);
                    string scene = Arg("--ng-scene", "growth");
                    sv.DebugContracts(scene);
                    var clock = sv.DebugWorld.Clock; long from = clock.Now.Seconds;
                    if (scene != "board")
                    {
                        sv.DebugWorld.SetSpeed(Ravensguard.Sim.Core.SimSpeed.X8);
                        await Until(tree, () => clock.Now.Seconds >= from + (scene == "builder" ? 900 : 240), 60000);
                    }
                    sv.DebugWorld.SetSpeed(Ravensguard.Sim.Core.SimSpeed.Paused);
                    await Frames(tree, 30);
                    if (scene == "board") { sv.DebugOpenContracts(); await Frames(tree, 40); }
                    break;
                }
            case "business":
                {
                    // dev (P5-F): a real new game, a little of everything owned set up at the barrier (DevBusinesses), then --ng-scene ledger | business | stall | prospect | licence
                    var w = MakeWorld(Math.Min(years, 60), 7);
                    var d = NewGameServices.NewDraft(); d.Name = "Shot"; d.LibraryWorldId = w.Info.Id;
                    router.Push(new RegionBuildScreen(router, d, w));
                    if (!await Until(tree, () => router.Top is Ravensguard.Game.World.StreetView, 9000)) throw new Exception("business shot: the region build did not finish");
                    var sv = (Ravensguard.Game.World.StreetView)router.Top!; var rt = sv.Runtime!;
                    await Until(tree, () => rt.HourDone >= sv.DebugWorld.Clock.Now.TotalHours, 6000);
                    await Frames(tree, 60);
                    string scene = Arg("--ng-scene", "ledger");
                    sv.DebugBusinessShot(scene);
                    sv.DebugWorld.SetSpeed(Ravensguard.Sim.Core.SimSpeed.X8);
                    var clock = sv.DebugWorld.Clock; long from = clock.Now.Seconds;
                    await Until(tree, () => clock.Now.Seconds >= from + 3 * 3600, 60000);
                    sv.DebugWorld.SetSpeed(Ravensguard.Sim.Core.SimSpeed.Paused);
                    await Frames(tree, 90);
                    sv.DebugBusinessOpen(scene);
                    await Frames(tree, 240);                                          // the panel's view comes from a barrier
                    break;
                }
            case "inheritance":
                {
                    // dev (P5-H): a real new game as the tanner's widow (--ng-origin, default tanners_widow: her tannery on its lease with the husband's arrears, her children), then
                    // --ng-scene will (the will panel before the town's priest) | succession (the succession screen with the estate) | ledger (the Ledger's Property tab, inherited rows marked)
                    var w = MakeWorld(Math.Min(years, 60), 7);
                    var d = NewGameServices.NewDraft(); d.Name = "Shot"; d.LibraryWorldId = w.Info.Id; d.OriginId = Arg("--ng-origin", "tanners_widow"); d.Female = true;
                    router.Push(new RegionBuildScreen(router, d, w));
                    if (!await Until(tree, () => router.Top is Ravensguard.Game.World.StreetView, 9000)) throw new Exception("inheritance shot: the region build did not finish");
                    var sv = (Ravensguard.Game.World.StreetView)router.Top!; var rt = sv.Runtime!;
                    await Until(tree, () => rt.HourDone >= sv.DebugWorld.Clock.Now.TotalHours, 6000);
                    await Frames(tree, 90);
                    sv.DebugInheritance(Arg("--ng-scene", "will"));
                    await Frames(tree, 240);                                          // the views come from a barrier
                    break;
                }
            case "treatment":
                {
                    // dev (P6-B1): a real new game, the hero hurt (a cut forearm vein, a deep cut across the chest) with bandages and a splint in his pack, then the treatment screen over him
                    var w = MakeWorld(Math.Min(years, 60), 7);
                    var d = NewGameServices.NewDraft(); d.Name = "Shot"; d.LibraryWorldId = w.Info.Id;
                    router.Push(new RegionBuildScreen(router, d, w));
                    if (!await Until(tree, () => router.Top is Ravensguard.Game.World.StreetView, 9000)) throw new Exception("treatment shot: the region build did not finish");
                    var sv = (Ravensguard.Game.World.StreetView)router.Top!; var rt = sv.Runtime!;
                    await Until(tree, () => rt.HourDone >= sv.DebugWorld.Clock.Now.TotalHours, 6000);
                    await Frames(tree, 60);
                    sv.DebugWorld.SetSpeed(Ravensguard.Sim.Core.SimSpeed.Paused);
                    sv.DebugTreatment();
                    await Frames(tree, 120);                                          // the view comes from a barrier
                    break;
                }
            case "family":
                {
                    // dev (P6-B3): a real new game as a man (--ng-origin, default discharged_soldier), then --ng-scene panel (married, two children, a course under way: the Family panel) | talk (the courting menu
                    // in the talk window) | empty (the panel of a hero with no family)
                    var w = MakeWorld(Math.Min(years, 60), 7);
                    var d = NewGameServices.NewDraft(); d.Name = "Shot"; d.LibraryWorldId = w.Info.Id; d.OriginId = Arg("--ng-origin", "discharged_soldier");
                    router.Push(new RegionBuildScreen(router, d, w));
                    if (!await Until(tree, () => router.Top is Ravensguard.Game.World.StreetView, 9000)) throw new Exception("family shot: the region build did not finish");
                    var sv = (Ravensguard.Game.World.StreetView)router.Top!; var rt = sv.Runtime!;
                    await Until(tree, () => rt.HourDone >= sv.DebugWorld.Clock.Now.TotalHours, 6000);
                    await Frames(tree, 90);
                    sv.DebugFamily(Arg("--ng-scene", "panel"), outPath);
                    await Frames(tree, 240);                                          // the views come from a barrier
                    break;
                }
            case "court":
                {
                    // dev (P6-C1): the Court panel over a made-up docket
                    var view = Ravensguard.Game.UI.CourtScreen.Demo();
                    router.Push(new Ravensguard.Game.UI.CourtScreen(router, () => view, (_, _) => { }, _ => { }, () => { }));
                    await Frames(tree, 40);
                    break;
                }
            case "workboard":
            case "workboard_mine":
                {
                    // dev (P6-C2): the contracts screen over a made-up board (workboard_mine: on the Taken tab, with the evidence and the pin)
                    var view = Ravensguard.Game.UI.ContractsScreen.Demo();
                    router.Push(new Ravensguard.Game.UI.ContractsScreen(router, () => view, new Ravensguard.Game.UI.ContractActions(_ => { }, _ => { }, (_, _) => { }, _ => { }, _ => { }, _ => { }), () => (390_000, 290_000)) { StartMine = what == "workboard_mine" });
                    await Frames(tree, 40);
                    break;
                }
            case "bondage":
            case "bondage_slaves":
            case "bondage_bond":
                {
                    // dev (P6-D1): the bondage panel over a made-up pen (the pen, the hero's two slaves, the enslaved hero's own bond)
                    var view = Ravensguard.Game.UI.BondageScreen.Demo(what == "bondage_bond");
                    router.Push(new Ravensguard.Game.UI.BondageScreen(router, new Ravensguard.Game.UI.BondageLink
                    {
                        View = () => view, Buy = _ => { }, Sell = _ => { }, Free = _ => { }, Treat = (_, _) => { }, Chain = (_, _) => { }, Work = (_, _) => { }, Escape = () => { }, Run = () => { }, SelfBuy = () => { },
                    }) { StartTab = what == "bondage" ? 0 : what == "bondage_slaves" ? 1 : 2 });
                    await Frames(tree, 40);
                    break;
                }
            case "heist":
                {
                    // dev (P6-D3): the heist panel over a castle's dungeon at night, in a real new game
                    var w = MakeWorld(Math.Min(years, 60), 7);
                    var d = NewGameServices.NewDraft(); d.Name = "Shot"; d.LibraryWorldId = w.Info.Id;
                    router.Push(new RegionBuildScreen(router, d, w));
                    if (!await Until(tree, () => router.Top is Ravensguard.Game.World.StreetView, 9000)) throw new Exception("heist shot: the region build did not finish");
                    var sv = (Ravensguard.Game.World.StreetView)router.Top!; var rt = sv.Runtime!;
                    await Until(tree, () => rt.HourDone >= sv.DebugWorld.Clock.Now.TotalHours, 6000);
                    await Frames(tree, 60);
                    sv.DebugWorld.SetSpeed(Ravensguard.Sim.Core.SimSpeed.Paused);
                    sv.DebugHeist();
                    await Frames(tree, 60);
                    break;
                }
            case "cell":
                {
                    // dev (P6-C1): the gaol cell of a held hero, with the sentence to expect and the ways out
                    var w = MakeWorld(Math.Min(years, 60), 7);
                    var d = NewGameServices.NewDraft(); d.Name = "Shot"; d.LibraryWorldId = w.Info.Id;
                    router.Push(new RegionBuildScreen(router, d, w));
                    if (!await Until(tree, () => router.Top is Ravensguard.Game.World.StreetView, 9000)) throw new Exception("cell shot: the region build did not finish");
                    var sv = (Ravensguard.Game.World.StreetView)router.Top!; var rt = sv.Runtime!;
                    await Until(tree, () => rt.HourDone >= sv.DebugWorld.Clock.Now.TotalHours, 6000);
                    await Frames(tree, 60);
                    sv.DebugWorld.SetSpeed(Ravensguard.Sim.Core.SimSpeed.Paused);
                    sv.DebugShowAftermath(2);
                    await Frames(tree, 60);
                    break;
                }
            case "faith":
                {
                    // dev (P6-B4): a real new game, the hero gives alms and takes a pilgrim's vow, then the Faith panel opens over him
                    var w = MakeWorld(Math.Min(years, 60), 7);
                    var d = NewGameServices.NewDraft(); d.Name = "Shot"; d.LibraryWorldId = w.Info.Id;
                    router.Push(new RegionBuildScreen(router, d, w));
                    if (!await Until(tree, () => router.Top is Ravensguard.Game.World.StreetView, 9000)) throw new Exception("faith shot: the region build did not finish");
                    var sv = (Ravensguard.Game.World.StreetView)router.Top!; var rt = sv.Runtime!;
                    await Until(tree, () => rt.HourDone >= sv.DebugWorld.Clock.Now.TotalHours, 6000);
                    await Frames(tree, 60);
                    sv.DebugWorld.SetSpeed(Ravensguard.Sim.Core.SimSpeed.Paused);
                    sv.DebugFaith();
                    await Frames(tree, 150);                                          // the view comes from a barrier
                    break;
                }
            case "faction":
                {
                    // dev (P6-D2): a real new game with a war on, a truce, the lords' opinions, a vacant fief and a record of service staged; the Faction screen opens on its Actions tab
                    var w = MakeWorld(Math.Min(years, 60), 7);
                    var d = NewGameServices.NewDraft(); d.Name = "Shot"; d.LibraryWorldId = w.Info.Id;
                    router.Push(new RegionBuildScreen(router, d, w));
                    if (!await Until(tree, () => router.Top is Ravensguard.Game.World.StreetView, 9000)) throw new Exception("faction shot: the region build did not finish");
                    var sv = (Ravensguard.Game.World.StreetView)router.Top!; var rt = sv.Runtime!;
                    await Until(tree, () => rt.HourDone >= sv.DebugWorld.Clock.Now.TotalHours, 6000);
                    await Frames(tree, 60);
                    sv.DebugWorld.SetSpeed(Ravensguard.Sim.Core.SimSpeed.Paused);
                    string tab = System.Environment.GetEnvironmentVariable("RG_FACTION_TAB") ?? "";
                    sv.DebugFaction(tab.Equals("opinions", StringComparison.OrdinalIgnoreCase) ? Ravensguard.Game.UI.FactionScreen.Tab.Opinions : tab.Equals("treaties", StringComparison.OrdinalIgnoreCase) ? Ravensguard.Game.UI.FactionScreen.Tab.Treaties : tab.Equals("realm", StringComparison.OrdinalIgnoreCase) ? Ravensguard.Game.UI.FactionScreen.Tab.Realm : Ravensguard.Game.UI.FactionScreen.Tab.Actions);
                    await Frames(tree, 150);                                          // the view comes from a barrier
                    break;
                }
            case "rule":
                {
                    // dev (P6-C3): a real new game, the hero made lord of his fief with a thief for a bailiff, a relief in arrears and a loan, then the Ledger opens on the Fief tab
                    var w = MakeWorld(Math.Min(years, 60), 7);
                    var d = NewGameServices.NewDraft(); d.Name = "Shot"; d.LibraryWorldId = w.Info.Id;
                    router.Push(new RegionBuildScreen(router, d, w));
                    if (!await Until(tree, () => router.Top is Ravensguard.Game.World.StreetView, 9000)) throw new Exception("rule shot: the region build did not finish");
                    var sv = (Ravensguard.Game.World.StreetView)router.Top!; var rt = sv.Runtime!;
                    await Until(tree, () => rt.HourDone >= sv.DebugWorld.Clock.Now.TotalHours, 6000);
                    await Frames(tree, 60);
                    sv.DebugWorld.SetSpeed(Ravensguard.Sim.Core.SimSpeed.Paused);
                    sv.DebugRule();
                    await Frames(tree, 150);                                          // the view comes from a barrier
                    break;
                }
            case "disease":
                {
                    // dev (P6-C4): a real new game, the hero is ill with a party member nursing him, the plague is in the region and wages are dear: the Character screen
                    var w = MakeWorld(Math.Min(years, 60), 7);
                    var d = NewGameServices.NewDraft(); d.Name = "Shot"; d.LibraryWorldId = w.Info.Id;
                    router.Push(new RegionBuildScreen(router, d, w));
                    if (!await Until(tree, () => router.Top is Ravensguard.Game.World.StreetView, 9000)) throw new Exception("disease shot: the region build did not finish");
                    var sv = (Ravensguard.Game.World.StreetView)router.Top!; var rt = sv.Runtime!;
                    await Until(tree, () => rt.HourDone >= sv.DebugWorld.Clock.Now.TotalHours, 6000);
                    await Frames(tree, 60);
                    sv.DebugWorld.SetSpeed(Ravensguard.Sim.Core.SimSpeed.Paused);
                    sv.DebugDisease();
                    await Frames(tree, 150);                                          // the view comes from a barrier
                    sv.DebugOpen("character");
                    await Frames(tree, 30);
                    break;
                }
            case "loadreport":
                {
                    // dev: the pre-load report of a save made with other content (a made-up one: a good added, one moved, one written off, a mod gone)
                    var rep = new Ravensguard.Sim.Save.LoadReport
                    {
                        ContentChanged = true, LayersChecked = true,
                        MissingMods = [new Ravensguard.Sim.Save.ModEntry("old_tools", "1.2.0")], ExtraMods = [new Ravensguard.Sim.Save.ModEntry("fine_cloth", "0.3.0")],
                        Changes =
                        [
                            "Content table 'good' changed since this save (3 new; moved aa_old_scrap -> scrap_iron; no longer in the game: zz_relic); the save was mapped onto it by name.",
                            "Content table 'craft_recipe' changed since this save (1 new; moved bake_bread_old -> bake_bread); the save was mapped onto it by name.",
                            "Warning: goods no longer in the game were written off (quantity in grams, or pieces): zz_relic 3,700. Their ledger columns went with their stock, so the mass and coin audits stay exact.",
                        ],
                    };
                    LoadReportDialog.Ask(router, rep, false, () => { }, () => { });
                    await Frames(tree, 6);
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
