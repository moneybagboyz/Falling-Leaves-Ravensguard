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
    public const string ContentHash = "B8342AD2275C1A22";
    public const int Seed0 = 20000, Seeds = 200;

    public static readonly Dictionary<AbstractScenarios.Scenario, long[]> Tallies = new()
    {
        [AbstractScenarios.Scenario.A] = [200, 30, 170, 28518, 762, 183, 4000, 405, 721, 0, 0, 0, 123, 632, 7, 0, 406, 211, 4000, 142, 388, 0, 0, 0, 84, 295, 0, 27],
        [AbstractScenarios.Scenario.B] = [200, 105, 94, 21183, 815, 729, 6000, 246, 814, 0, 0, 0, 280, 475, 9, 51, 685, 1547, 10000, 432, 674, 0, 0, 0, 91, 527, 52, 15],
        [AbstractScenarios.Scenario.C] = [200, 192, 7, 63106, 792, 290, 6000, 223, 566, 0, 0, 0, 52, 428, 2, 310, 1859, 2599, 5000, 668, 1600, 0, 0, 0, 189, 1083, 0, 587],
        [AbstractScenarios.Scenario.D] = [200, 200, 0, 97962, 18, 5, 2400, 5, 3, 0, 0, 0, 2, 14, 1, 1, 1506, 155, 4000, 346, 261, 0, 0, 0, 835, 598, 0, 73],
        [AbstractScenarios.Scenario.DMud] = [200, 50, 148, 302122, 978, 525, 2400, 127, 163, 0, 0, 0, 114, 768, 60, 36, 636, 614, 4000, 65, 82, 0, 0, 0, 187, 400, 0, 49],
        [AbstractScenarios.Scenario.AFlanked] = [200, 83, 117, 37059, 823, 296, 4000, 437, 746, 0, 0, 0, 110, 700, 11, 2, 907, 536, 4000, 297, 850, 0, 0, 0, 247, 606, 6, 48],
        [AbstractScenarios.Scenario.ANoLeader] = [200, 26, 173, 29448, 522, 278, 4000, 258, 480, 0, 0, 0, 79, 438, 5, 0, 301, 367, 4000, 115, 288, 0, 0, 0, 55, 225, 0, 21],
    };

    public static AbstractEquivalence.Tally Of(AbstractScenarios.Scenario s) => AbstractEquivalence.Unpack(Tallies[s]);
}
