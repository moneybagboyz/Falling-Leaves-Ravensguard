using Ravensguard.Sim.Carts;
using Ravensguard.Sim.Core;

namespace Ravensguard.Sim.Naval;

/// <summary>Sea state, capsizing and swamping at sea (ships-and-naval-war 4.2).</summary>
public static class SeaState
{
    /// <summary>Waves, mm: <c>wind m/s x min(fetch km, 50) x 1000 / 250</c> (10 m/s over 50 km: 2 m).</summary>
    public static int WaveMm(SeaRules r, int windMps, int fetchKm) => (int)((long)windMps * Math.Min(Math.Max(0, fetchKm), r.D.FetchCapKm) * 1000 / r.D.WaveDiv);

    /// <summary>The fetch, km: whole kilometres of open sea upwind of a point (<paramref name="isSea"/> at a point in mm), at most the cap. <paramref name="windFromQ24"/> is the way the wind comes from.</summary>
    public static int FetchKm(SeaRules r, Func<long, long, bool> isSea, long xMm, long yMm, int windFromQ24)
    {
        var (c, s) = FrameMath.CosSin(windFromQ24);
        int km = 0;
        for (int i = 1; i <= r.D.FetchCapKm; i++)
        {
            long x = xMm + i * 1_000_000L * c / IntMath.One30, y = yMm + i * 1_000_000L * s / IntMath.One30;
            if (!isSea(x, y)) break;
            km = i;
        }
        return km;
    }

    /// <summary>A beam sea: the waves, which run with the wind, come within 45 degrees either side of abeam.</summary>
    public static bool IsBeamSea(SeaRules r, int headingQ24, int windToQ24)
    {
        int d = Sailing.OffAsternQ24(headingQ24, windToQ24);
        return Math.Abs(FrameMath.CosSin(d).Sin) * 1000 >= (long)r.D.BeamSeaSinPm * IntMath.One30;
    }

    /// <summary>
    /// The Phase 5 capsize check at sea, one every 20 game seconds (<paramref name="gameSec"/> / 20 keys it): GM at or under 0 rolls her over; GM under the tender line in a beam sea of 10 m/s or more
    /// capsizes with 20 permille (stream 0x7D0, keyed on the ship). A steady ship, or a tender one in any other sea, never does.
    /// </summary>
    public static bool Capsizes(ulong seed, EntityId ship, long gameSec, int gmMm, int windMps, bool beamSea, WaterTuning wt)
    {
        if (gmMm <= 0) return true;
        if (gmMm >= wt.TenderGmMm || windMps < wt.BeamSeaWindMps || !beamSea) return false;
        return DetRng.Range(seed, gameSec / 20, ship.Value, RngStreams.BeamSeaCapsize, 0, 0, 1000) < wt.CapsizePm;
    }

    /// <summary>Water shipped, L/s: 20 for each metre the waves stand over the freeboard (a fraction of it pro rata), and 20 for each broken hull cell.</summary>
    public static int InflowLps(WaterTuning wt, int waveMm, int freeboardMm, int brokenCells) => Math.Max(0, waveMm - freeboardMm) * wt.SwampLps / 1000 + brokenCells * wt.SwampLps;

    /// <summary>Water bailed, L/s: 5 for a man with a bucket, 15 for a bilge pump (worked by 2 men, who are counted in <paramref name="pumpMen"/>).</summary>
    public static int BailLps(WaterTuning wt, int bailers, int pumps) => bailers * wt.BailLps + pumps * 15;

    /// <summary>The water in the hull after <paramref name="seconds"/>: never below none.</summary>
    public static long WaterAfter(long waterL, int inflowLps, int bailLps, int seconds) => Math.Max(0, waterL + (long)(inflowLps - bailLps) * seconds);

    /// <summary>The freeboard with <paramref name="waterL"/> litres in her (a litre is a kilo: the draft grows with it); at 0 she founders.</summary>
    public static int FreeboardNow(BoatLoad load, long waterL, long waterplaneMm2, int blockPm) =>
        waterL <= 0 ? load.FreeboardMm : load.FreeboardMm - (int)Math.Min(int.MaxValue / 2, (long)BoatMath.DraftMm(waterL, waterplaneMm2, blockPm));
}

public enum ShoreKind : byte { Open, Sand, Rock }

/// <summary>What a storm did to a ship that could not claw off a lee shore.</summary>
public enum LeeFate : byte { Safe, Beached, Wrecked, Aground }

public sealed record LeeResult(LeeFate Fate, int SecondsToShore, int DriftMmps, int NetUpwindMmps, int[] BrokenCells);

/// <summary>
/// Storms and lee shores (4.2): in a wind of 17 m/s or more a ship drifts downwind at 4% of its speed; a ship that cannot make that good upwind (oars straight into it, a square sail gives none) drives ashore;
/// on sand it is beached, on rocks hull cells break by stream 0x7D1 and from two it is wrecked. A master with Seamanship 40 or more heaves to (a drogue: the drift is two thirds).
/// </summary>
public static class LeeShore
{
    /// <summary>The ship's best way upwind, mm/s: rowing straight into the wind against its windage, or on a beam reach under sail (no progress to windward, only the leeway lost).</summary>
    public static int UpwindMmps(SeaShip s, SeaCrewState c, int windMps)
    {
        var row = Sailing.Read(s, new SeaCrewState { OarsOut = c.OarsOut, Hard = true, StaminaPm = c.StaminaPm, RowersManned = c.RowersManned }, 0, FrameMath.Turn / 2, windMps);
        return row.SpeedMmps;
    }

    public static int DriftMmps(SeaRules r, int windMps, int masterSeamanship) => windMps * r.D.StormDriftPm * (masterSeamanship >= 40 ? 667 : 1000) / 1000;

    public static LeeResult Resolve(ulong seed, SeaShip s, SeaCrewState crew, int windMps, int masterSeamanship, ShoreKind shore, long distMm)
    {
        var r = s.Rules;
        if (shore == ShoreKind.Open || windMps < r.D.StormMps) return new LeeResult(LeeFate.Safe, 0, 0, 0, []);
        int drift = DriftMmps(r, windMps, masterSeamanship), up = UpwindMmps(s, crew, windMps), net = up - drift;
        if (net > 0 || drift <= 0) return new LeeResult(LeeFate.Safe, 0, drift, net, []);
        int secs = (int)Math.Min(int.MaxValue, Voyages.LegSeconds(Math.Max(0, distMm), drift));
        if (shore == ShoreKind.Sand) return new LeeResult(LeeFate.Beached, secs, drift, net, []);
        var broken = new List<int>();
        for (int cell = 0; cell < s.Stats.HullCells; cell++)
            if (DetRng.Range(seed, 0, s.Id.Value, RngStreams.WreckDamage, (uint)cell, 0, 1000) < r.D.WreckPm) broken.Add(cell);
        return new LeeResult(broken.Count >= r.D.WreckMinCells ? LeeFate.Wrecked : LeeFate.Aground, secs, drift, net, [.. broken]);
    }
}
