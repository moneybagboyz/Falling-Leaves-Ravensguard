using Ravensguard.Sim.Combat.Abstract;

namespace Ravensguard.Sim.Tests.Combat.Abstract;

/// <summary>
/// The live fights of the equivalence scenarios on the fresh seeds (abstract-battles-v1 4.4; seeds 20000 to 20199, none of them in the fit set 1000 on), summed: the tactical side of
/// the fast AB-3 check, so the normal suite runs only the abstract battles. Made by <c>tools/Ravensguard.Calibrate -- --abstract reference --seeds 200 --seed0 20000</c>; the Soak
/// test refights them and must give these numbers exactly. <see cref="ContentHash"/> is the content they were fought on (the wound tables' hash): another hash is a stale reference.
/// Each line: n, side 0 wins, side 1 wins, duration x10, then per side casualties, fled, start, died after, first pass and the seven worst-class counts.
/// </summary>
public static class TacticalReference
{
    public const string ContentHash = "3972AD104E0FFE93";
    public const int Seed0 = 20000, Seeds = 200;

    public static readonly Dictionary<AbstractScenarios.Scenario, long[]> Tallies = new()
    {
        [AbstractScenarios.Scenario.A] = [200, 51, 149, 28655, 692, 199, 4000, 378, 646, 0, 0, 0, 113, 567, 12, 0, 513, 309, 4000, 147, 493, 0, 0, 0, 127, 356, 0, 30],
        [AbstractScenarios.Scenario.B] = [200, 117, 83, 20541, 782, 823, 6000, 217, 780, 0, 0, 0, 316, 448, 4, 14, 711, 1314, 10000, 443, 708, 0, 0, 0, 103, 543, 53, 12],
        [AbstractScenarios.Scenario.C] = [200, 188, 11, 52189, 784, 379, 6000, 289, 642, 0, 0, 0, 52, 475, 0, 257, 2037, 2466, 5000, 818, 1861, 0, 0, 0, 200, 1146, 0, 691],
        [AbstractScenarios.Scenario.D] = [200, 200, 0, 96711, 20, 6, 2400, 5, 3, 0, 0, 0, 2, 15, 1, 2, 1543, 144, 4000, 360, 251, 0, 0, 0, 854, 614, 0, 75],
        [AbstractScenarios.Scenario.DMud] = [200, 46, 152, 300920, 1015, 558, 2400, 153, 162, 0, 0, 0, 131, 772, 72, 40, 643, 642, 4000, 74, 81, 0, 0, 0, 182, 415, 0, 46],
        [AbstractScenarios.Scenario.AFlanked] = [200, 120, 78, 36948, 753, 324, 4000, 412, 680, 0, 0, 0, 105, 639, 9, 0, 1050, 498, 4000, 369, 1001, 0, 0, 0, 304, 692, 2, 52],
        [AbstractScenarios.Scenario.ANoLeader] = [200, 56, 144, 37594, 521, 385, 4000, 261, 453, 0, 0, 0, 75, 437, 9, 0, 411, 480, 4000, 124, 390, 0, 0, 0, 91, 293, 0, 27],
    };

    public static AbstractEquivalence.Tally Of(AbstractScenarios.Scenario s) => AbstractEquivalence.Unpack(Tallies[s]);
}
