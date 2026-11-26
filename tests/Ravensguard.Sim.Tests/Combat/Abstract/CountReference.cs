namespace Ravensguard.Sim.Tests.Combat.Abstract;

/// <summary>
/// The v2 resolver on the 40 count scenarios of AW-10 (<c>CountScenarios.Gate</c>) on fresh seeds (5000 on; the tables were fitted on seeds 100 on): the reference the count battle is gated against, so the
/// normal suite runs only count battles. Made by <c>tools/Ravensguard.Calibrate -- --abstract count reference</c>; the soak test refights a few of them and must give these numbers exactly.
/// <see cref="ContentHash"/> is the content they were fought on (the wound tables' hash), <see cref="TablesFingerprint"/> the count tables they were measured against: another value is a stale reference.
/// </summary>
public static class CountReference
{
    public const string ContentHash = "51660CE79CF95FD6";
    public const string TablesFingerprint = "FCAD605E9FC0F1EE";
    public const int Seed0 = 5000;
    // n, side 0 wins, side 1 wins, duration x1000 s (sum), casualties x1 of side 0 (sum), of side 1 (sum)
    public static readonly long[][] Tallies =
    [
        [32, 18, 14, 488000, 374, 423],
        [32, 32, 0, 193000, 42, 745],
        [16, 16, 0, 183000, 1050, 4671],
        [32, 0, 32, 215000, 476, 42],
        [16, 5, 11, 437000, 329, 1860],
        [32, 31, 1, 579000, 677, 984],
        [16, 16, 0, 92000, 71, 770],
        [32, 0, 32, 373000, 760, 166],
        [16, 0, 16, 100000, 610, 25],
        [32, 12, 20, 484000, 623, 614],
        [16, 0, 16, 94000, 1929, 512],
        [32, 20, 12, 628000, 372, 479],
        [32, 32, 0, 222000, 451, 1214],
        [16, 16, 0, 96000, 63, 2084],
        [32, 32, 0, 362000, 248, 2632],
        [16, 0, 16, 96000, 1603, 52],
        [32, 31, 1, 545000, 418, 1780],
        [16, 1, 15, 483000, 2485, 1255],
        [32, 0, 32, 381000, 1434, 512],
        [16, 16, 0, 163000, 486, 1669],
        [32, 32, 0, 387000, 131, 452],
        [16, 0, 16, 178000, 1764, 599],
        [32, 2, 30, 507000, 363, 379],
        [32, 22, 10, 1039000, 1297, 2292],
        [16, 0, 16, 347000, 8793, 2051],
        [32, 32, 0, 245000, 510, 1539],
        [16, 16, 0, 188000, 572, 5208],
        [32, 0, 32, 206000, 648, 32],
        [16, 16, 0, 108000, 134, 1974],
        [32, 32, 0, 469000, 11, 998],
        [16, 0, 16, 214000, 2318, 7],
        [32, 2, 29, 661000, 893, 507],
        [16, 16, 0, 80000, 29, 741],
        [32, 0, 32, 292000, 546, 36],
        [16, 0, 16, 107000, 1090, 238],
        [16, 16, 0, 106000, 621, 4903],
        [32, 32, 0, 476000, 337, 1467],
        [16, 0, 16, 224000, 3681, 1654],
        [32, 12, 20, 720000, 1626, 2503],
        [16, 15, 1, 475000, 1999, 3006],
    ];
}
