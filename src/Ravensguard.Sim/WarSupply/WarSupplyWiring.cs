using Ravensguard.Sim.Save;
using Ravensguard.Sim.Worlds;

namespace Ravensguard.Sim.WarSupply;

/// <summary>Installs the army economy's interface on the home region (a hook in <see cref="WorldRuntime"/>): only when the world's state already holds a book (a save with a <c>warsupply</c> section); the harness and the military make one with <see cref="Ensure"/>.</summary>
public static class WarSupplyWiring
{
    public static HostSupply? Attach(RegionWorld home, WorldState state)
    {
        if (state.WarSupply == null) return null;
        return HostSupply.Install(home.Economy, state.WarSupply);
    }

    /// <summary>The service of the running home region, made (with the state's book) on first use.</summary>
    public static HostSupply Ensure(RegionWorld home, WorldState state)
    {
        if (home.Sim.Get<HostSupply>() is { } hs) return hs;
        state.WarSupply ??= new WarSupplyBook();
        return HostSupply.Install(home.Economy, state.WarSupply);
    }
}
