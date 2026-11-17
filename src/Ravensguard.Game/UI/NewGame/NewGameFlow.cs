using Ravensguard.Sim.Worlds;

namespace Ravensguard.Game.UI.NewGame;

public enum WorldPurpose { Play, Legends }

/// <summary>Routing between the new-game screens. Stateless: the draft carries every choice.</summary>
public static class NewGameFlow
{
    /// <summary>Start button: use the cached or generated world, then embark (auto pick, or the embark map when "Choose region" was pressed).</summary>
    public static void Begin(ScreenRouter router, NewGameDraft d)
    {
        if (d.LibraryWorldId is { } id && NewGameServices.Cached(id) is { } w) { AfterWorld(router, d, w, replace: false); return; }
        router.Push(new WorldCreationScreen(router, d, WorldPurpose.Play));
    }

    public static void AfterWorld(ScreenRouter router, NewGameDraft d, LoadedWorld w, bool replace)
    {
        NewGameServices.Remember(w);
        d.LibraryWorldId = w.Info.Id; // Back then Start reuses this world instead of creating a second one
        Godot.Control next = d.ChooseRegion ? new EmbarkMapScreen(router, d, w) : new RegionBuildScreen(router, d, w);
        if (replace) router.Replace(next); else router.Push(next);
    }

    /// <summary>Legends of a library world: loads it (showing the chronicle unfold) and opens the viewer.</summary>
    public static void OpenLegends(ScreenRouter router, WorldInfo info)
    {
        if (NewGameServices.Cached(info.Id) is { } w) router.Push(new LegendsScreen(router, w));
        else router.Push(new WorldCreationScreen(router, info, WorldPurpose.Legends));
    }
}
