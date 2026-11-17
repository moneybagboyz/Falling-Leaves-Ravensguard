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
            // the region build ends in the street view (see RegionBuildScreen TODO)
            var d2 = NewGameServices.NewDraft(); d2.Name = "Smoke Tester"; d2.LibraryWorldId = w.Id;
            router.Push(new RegionBuildScreen(router, d2, loaded));
            if (!await Until(tree, () => router.Top is Ravensguard.Game.World.StreetView, 6000)) throw new Exception("region build did not finish");
            router.Pop(); await Frames(tree, 1);
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
