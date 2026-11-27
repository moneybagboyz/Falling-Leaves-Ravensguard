namespace Ravensguard.Sim.Tests.Combat.Abstract;

/// <summary>
/// The v2 resolver on the 40 count scenarios of AW-10 (<c>CountScenarios.Gate</c>) on fresh seeds (5000 on; the tables were fitted on seeds 100 on): the reference the count battle is gated against, so the
/// normal suite runs only count battles. Made by <c>tools/Ravensguard.Calibrate -- --abstract count reference</c>; the soak test refights a few of them and must give these numbers exactly.
/// <see cref="ContentHash"/> is the content they were fought on (the wound tables' hash), <see cref="TablesFingerprint"/> the count tables they were measured against: another value is a stale reference.
/// </summary>
public static class CountReference
{
    public const string ContentHash = "51660CE79CF95FD6";
    public const string TablesFingerprint = "0B86BEEA3342DB8C";
    public const int Seed0 = 5000;
    // n, side 0 wins, side 1 wins, duration x1000 s (sum), casualties x1 of side 0 (sum), of side 1 (sum)
    public static readonly long[][] Tallies =
    [
        [32, 18, 13, 339000, 265, 296],
        [32, 32, 0, 375000, 165, 618],
        [16, 16, 0, 121000, 325, 2647],
        [32, 0, 32, 258000, 529, 64],
        [16, 16, 0, 297000, 62, 715],
        [32, 0, 32, 656000, 463, 86],
        [16, 16, 0, 111000, 97, 824],
        [32, 2, 30, 281000, 533, 83],
        [16, 0, 16, 202000, 442, 128],
        [32, 9, 23, 306000, 392, 231],
        [16, 0, 16, 134000, 953, 298],
        [32, 20, 9, 495000, 254, 360],
        [32, 27, 4, 326000, 553, 506],
        [16, 16, 0, 112000, 50, 2242],
        [32, 32, 0, 276000, 43, 1827],
        [16, 0, 16, 112000, 1674, 37],
        [32, 32, 0, 400000, 116, 814],
        [16, 1, 15, 358000, 957, 304],
        [32, 1, 30, 331000, 501, 253],
        [16, 16, 0, 105000, 149, 966],
        [32, 32, 0, 296000, 57, 365],
        [16, 0, 16, 104000, 1011, 143],
        [32, 21, 11, 518000, 224, 313],
        [32, 30, 2, 689000, 250, 896],
        [16, 0, 16, 240000, 4591, 541],
        [32, 30, 0, 356000, 257, 1181],
        [16, 16, 0, 157000, 119, 4089],
        [32, 0, 32, 252000, 785, 33],
        [16, 16, 0, 70000, 22, 1179],
        [32, 32, 0, 568000, 5, 1039],
        [16, 0, 16, 241000, 2145, 3],
        [32, 0, 32, 438000, 599, 124],
        [16, 16, 0, 87000, 34, 746],
        [32, 0, 32, 237000, 454, 19],
        [16, 0, 16, 75000, 579, 120],
        [16, 16, 0, 77000, 153, 2424],
        [32, 24, 8, 320000, 352, 611],
        [16, 16, 0, 316000, 342, 1995],
        [32, 2, 30, 328000, 739, 331],
        [16, 3, 13, 268000, 1386, 1164],
    ];
}
