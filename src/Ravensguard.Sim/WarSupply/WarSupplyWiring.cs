using Ravensguard.Sim.Save;
using Ravensguard.Sim.Worlds;

namespace Ravensguard.Sim.WarSupply;

/// <summary>Installs the army economy's interface on the home region (a hook in <see cref="WorldRuntime"/>): only when the world's state already holds a book (a save with a <c>warsupply</c> section); the harness and the military make one with <see cref="Ensure"/>.</summary>
public static class WarSupplyWiring
{
    public static HostSupply? Attach(RegionWorld home, WorldState state)
    {
        if (state.WarSupply == null) return null;
        var hs = HostSupply.Install(home.Economy, state.WarSupply);
        hs.DiseaseSource = () => state.Disease;
        hs.ArmsSource = () => ArmsOf(state);
        return hs;
    }

    /// <summary>The service of the running home region, made (with the state's book) on first use.</summary>
    public static HostSupply Ensure(RegionWorld home, WorldState state)
    {
        if (home.Sim.Get<HostSupply>() is { } hs) return hs;
        state.WarSupply ??= new WarSupplyBook();
        var made = HostSupply.Install(home.Economy, state.WarSupply);
        made.DiseaseSource = () => state.Disease;
        made.ArmsSource = () => ArmsOf(state);
        return made;
    }

    /// <summary>The arms industry of the state (its book is the war book's), without armouries: the trade needs none.</summary>
    private static ArmsWorld? ArmsOf(WorldState state) =>
        state.WarSupply == null || state.Content == null || state.Economy == null ? null : new ArmsWorld(state.WarSupply.Arms, state.Content, state.Economy.Items, null, state.Economy.Goods, state.Seed);
}
