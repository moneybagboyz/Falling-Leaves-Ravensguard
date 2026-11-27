using Ravensguard.Sim.Core;

namespace Ravensguard.Sim.Naval;

/// <summary>The crew orders the helm shows (war screens 3.1): row, ship oars, ready grapples, board. Append only.</summary>
public enum CrewOrder : byte { Row, ShipOars, ReadyGrapples, Board }

/// <summary>
/// What the helm HUD shows (P8-WS-1, war screens 3.1), every figure read from the ship's frame and state: the wind (from, in degrees, and m/s), the heading in degrees, the speed over ground (mm/s, the frame's own),
/// the sail set in m2, the sea state (waves in mm and a text key), the water in the hull in litres, and which crew orders are in force. Built with integers only; the HUD draws it and holds nothing.
/// </summary>
public sealed record HelmView(int WindFromDeg, int WindMps, int HeadingDeg, int SpeedMmps, int SailM2, int WaveMm, string SeaKey, long WaterL, IReadOnlyList<CrewOrder> Orders)
{
    /// <summary>The sea-state words by wave height: calm under 0.1 m, slight under 0.5, moderate under 1.25, rough under 2.5, high from there.</summary>
    public static string SeaKeyOf(int waveMm) => waveMm < 100 ? "hm_sea_calm" : waveMm < 500 ? "hm_sea_slight" : waveMm < 1250 ? "hm_sea_moderate" : waveMm < 2500 ? "hm_sea_rough" : "hm_sea_high";

    /// <summary>Degrees (0 to 359, rounded to the nearest) of a frame heading in turns of 2^24.</summary>
    public static int DegOf(int headingQ24) => (int)(((long)(headingQ24 & FrameMath.Mask) * 360 + FrameMath.Turn / 2) / FrameMath.Turn) % 360;

    /// <summary>Knots x 10 of a speed in mm/s (1 knot = 514.4 mm/s), for the HUD's second figure.</summary>
    public static int KnotsX10(int mmps) => (int)((long)mmps * 10_000 / 514_444);

    public static HelmView Of(ShipFrame frame, SeaShip ship, SeaCrewState crew, int windFromDeg, int windMps, int waveMm, long waterL, bool grapplesReady, bool boarding)
    {
        var orders = new List<CrewOrder>();
        orders.Add(crew.OarsOut ? CrewOrder.Row : CrewOrder.ShipOars);
        if (grapplesReady) orders.Add(CrewOrder.ReadyGrapples);
        if (boarding) orders.Add(CrewOrder.Board);
        return new HelmView(((windFromDeg % 360) + 360) % 360, windMps, DegOf(frame.Heading), frame.SpeedMmps, ship.SailM2, waveMm, SeaKeyOf(waveMm), waterL, orders);
    }
}
