using Ravensguard.Sim.Worlds;

namespace Ravensguard.Sim.Contracts;

/// <summary>
/// The contracts' hook on the running world's hours (contracts-and-rumours-v2 4.6): runs on the tick worker after the hour's own passes (as the law's hook does), so a hero's take or hand-over asked in an hour is
/// on the books before the next hour's verification. Hour 0: expiries, the companies' pass, a tournament's announcement, slander at a season's start; every hour: a settlement's needs, verification, the fair
/// day's bouts.
/// </summary>
public sealed class ContractLink
{
    public WorldRuntime Runtime { get; }
    private ContractLink(WorldRuntime rt) { Runtime = rt; }

    public static ContractLink Attach(WorldRuntime rt)
    {
        var l = new ContractLink(rt);
        rt.AfterHour += l.OnHour;
        return l;
    }

    private void OnHour(long hour) => ContractWiring.OnHour(Runtime.State, hour);
}
