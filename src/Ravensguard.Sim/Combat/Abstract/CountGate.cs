namespace Ravensguard.Sim.Combat.Abstract;

/// <summary>
/// The AW-10 gate's three tolerances (abstract-war 10), shared by the calibrate tool and the test: a scenario's mean casualties of a side within 10% of v2's (with a floor of 0.5% of the
/// side's start for tiny means, the way Phase 4's gate floors small counts), the win rate within 10 points, the duration within 25% (floor one second). Each returns the error in units of
/// its tolerance: 1 or less is inside the gate.
/// </summary>
public static class CountGate
{
    public static double CasErr(double v2, double count, int start) => Math.Abs(count - v2) / Math.Max(0.1 * v2, 0.005 * start);
    public static double WinErr(double v2, double count) => Math.Abs(count - v2) / 0.10;
    public static double DurErr(double v2, double count) => Math.Abs(count - v2) / Math.Max(0.25 * v2, 1.0);
}
