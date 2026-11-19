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
    public const string ContentHash = "51660CE79CF95FD6";
    public const int Seed0 = 20000, Seeds = 200;

    public static readonly Dictionary<AbstractScenarios.Scenario, long[]> Tallies = new()
    {
        [AbstractScenarios.Scenario.A] = [200, 51, 149, 28442, 687, 199, 4000, 375, 646, 0, 0, 0, 112, 563, 12, 0, 516, 300, 4000, 145, 498, 0, 0, 0, 127, 356, 0, 33],
        [AbstractScenarios.Scenario.B] = [200, 117, 83, 20541, 782, 823, 6000, 217, 780, 0, 0, 0, 316, 448, 4, 14, 711, 1314, 10000, 443, 708, 0, 0, 0, 103, 543, 53, 12],
        [AbstractScenarios.Scenario.C] = [200, 188, 11, 52115, 781, 378, 6000, 290, 640, 0, 0, 0, 52, 474, 0, 255, 2039, 2460, 5000, 822, 1863, 0, 0, 0, 199, 1147, 0, 693],
        [AbstractScenarios.Scenario.D] = [200, 200, 0, 96711, 20, 6, 2400, 5, 3, 0, 0, 0, 2, 15, 1, 2, 1543, 144, 4000, 360, 251, 0, 0, 0, 854, 614, 0, 75],
        [AbstractScenarios.Scenario.DMud] = [200, 46, 152, 300920, 1015, 558, 2400, 153, 162, 0, 0, 0, 131, 772, 72, 40, 643, 642, 4000, 74, 81, 0, 0, 0, 182, 415, 0, 46],
        [AbstractScenarios.Scenario.AFlanked] = [200, 120, 78, 36908, 752, 326, 4000, 411, 678, 0, 0, 0, 106, 637, 9, 0, 1053, 502, 4000, 362, 1005, 0, 0, 0, 308, 688, 2, 55],
        [AbstractScenarios.Scenario.ANoLeader] = [200, 56, 144, 37674, 522, 380, 4000, 262, 454, 0, 0, 0, 76, 437, 9, 0, 411, 485, 4000, 119, 390, 0, 0, 0, 91, 289, 0, 31],
    };

    public static AbstractEquivalence.Tally Of(AbstractScenarios.Scenario s) => AbstractEquivalence.Unpack(Tallies[s]);
}
