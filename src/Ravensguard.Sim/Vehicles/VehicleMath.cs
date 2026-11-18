using Ravensguard.Content;

namespace Ravensguard.Sim.Vehicles;

/// <summary>The rolling-resistance tables (permille of weight, per <see cref="Surface"/>), read from content; wheels and runners.</summary>
public sealed class VehicleTuning
{
    public int[] Wheel { get; } = new int[SurfaceModel.Count];
    public int[] Runner { get; } = new int[SurfaceModel.Count];
    public int PushNPerStrength = 6, StuckCrrMultPm = 1500, WheelWearPm = 1;

    /// <summary>The shipped numbers (spec 4.5), for the pure maths that has no content database (the abstract caravans).</summary>
    public static readonly VehicleTuning Default = Make(null);

    public int Crr(Surface s, bool runners) => (runners ? Runner : Wheel)[(int)s];

    public static VehicleTuning From(ContentDatabase db) => Make(db.All<VehicleTuningDef>().OrderBy(x => x.Id, StringComparer.Ordinal).FirstOrDefault());

    private static VehicleTuning Make(VehicleTuningDef? d)
    {
        var t = new VehicleTuning();
        int[] w = [25, 45, 70, 70, 110, 170, 260, 70, 200, 15, 150], r = [120, 150, 150, 160, 180, 220, 300, 30, 90, 12, 200];
        for (int i = 0; i < w.Length; i++) { t.Wheel[i] = w[i]; t.Runner[i] = r[i]; }
        if (d == null) return t;
        foreach (var s in Enum.GetValues<Surface>())
        {
            string k = ToSnake(s.ToString());
            if (d.CrrPm.TryGetValue(k, out int a)) t.Wheel[(int)s] = a;
            if (d.RunnerCrrPm.TryGetValue(k, out int b)) t.Runner[(int)s] = b;
        }
        t.PushNPerStrength = d.PushNPerStrength; t.StuckCrrMultPm = d.StuckCrrMultPm; t.WheelWearPm = d.WheelWearPm;
        return t;
    }

    private static string ToSnake(string s)
    {
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < s.Length; i++) { if (i > 0 && char.IsUpper(s[i])) sb.Append('_'); sb.Append(char.ToLowerInvariant(s[i])); }
        return sb.ToString();
    }
}

/// <summary>What one evaluation of the cart formula found (spec 4.5): the forces, the ratio, the factor and the speed, and the reasons it may not go.</summary>
public readonly record struct SpeedResult(long FReqMilliN, long FPullN, int RatioPm, int FPm, int SpeedCmps, int AxlePm, bool CannotMove, bool HitchRefused, bool OverAxle)
{
    /// <summary>Millimetres per game second (a game second is 1/20 of a real one at 1x: 250 cm/s = 125 mm/gs).</summary>
    public int SpeedMmPerGs => VehicleMath.MmPerGs(SpeedCmps);
}

/// <summary>
/// The one cart-speed formula (horses-and-carts spec 4.5). Integer maths only, no allocation: force needed from mass and resistance, the team's pull, the ratio of the two and the speed factor it
/// gives. The live tier calls it for the hero's cart and the abstract caravans call it for every leg, so a trip takes the same time whichever tier plays it (H-13, H-14).
/// </summary>
public static class VehicleMath
{
    /// <summary>Centimetres per real second to millimetres per game second (a real second is 20 game seconds at 1x: 250 cm/s = 2.5 m/s = 125 mm/gs).</summary>
    public static int MmPerGs(int cmps) => (cmps + 1) / 2;
    public static int CmpsOf(int mmPerGs) => mmPerGs * 2;

    /// <summary>Force to keep <paramref name="massKg"/> rolling, in millinewtons: <c>mass x 9.81 x (crr + slope + wind) / 1000</c>; clamped at 0, so a downhill never pushes (spec 4.5).</summary>
    public static long FReqMilliN(int massKg, int resistPm) => resistPm <= 0 || massKg <= 0 ? 0 : (long)massKg * 981 * resistPm / 100;

    /// <summary>Ratio of the force needed to the force the team gives, permille (nearest). A team of nothing is int.MaxValue.</summary>
    public static int RatioPm(long freqMilliN, long fpullN)
    {
        if (freqMilliN <= 0) return 0;
        if (fpullN <= 0) return int.MaxValue;
        long r = (freqMilliN + fpullN / 2) / fpullN;
        return r > int.MaxValue ? int.MaxValue : (int)r;
    }

    /// <summary>The speed factor (permille) of a ratio: 1000 up to 600, then a straight fall to 600 at 1000, then to 200 at 1300, then nothing (spec 4.5).</summary>
    public static int FPm(int ratioPm) => ratioPm <= 600 ? 1000 : ratioPm <= 1000 ? 1000 - (ratioPm - 600) : ratioPm <= 1300 ? 600 - (ratioPm - 1000) * 4 / 3 : 0;

    /// <summary>The pull of one animal after lameness and weakness: <c>pull x (1000 - lame) / 1000 x (1000 - weak) / 1000</c>.</summary>
    public static int EffectivePullN(int pullN, int lamePm, int weakPm) => (int)((long)pullN * (1000 - lamePm) / 1000 * (1000 - weakPm) / 1000);

    /// <summary>How much pull a lameness level takes away (permille): none, mild, severe, unusable.</summary>
    public static int LamePm(int level) => level switch { <= 0 => 0, 1 => 300, 2 => 600, _ => 1000 };

    /// <summary>
    /// The speed of a vehicle: <paramref name="baseCmps"/> x f(ratio) x the gait cap of its slowest animal, x0.8 above its axle limit, x0.7 on ice. <paramref name="grossKg"/> is the vehicle and its cargo (the axle's
    /// load), <paramref name="totalKg"/> adds the people on it. A slope or wind is part of <paramref name="resistPm"/>. Above 130 percent of the axle limit the hitch is refused.
    /// </summary>
    public static SpeedResult Evaluate(int baseCmps, int grossKg, int totalKg, int resistPm, long pullN, int gaitCapCmps, int axleLimitKg, bool ice)
    {
        long freq = FReqMilliN(totalKg, resistPm);
        int ratio = RatioPm(freq, pullN);
        int f = FPm(ratio);
        int axlePm = axleLimitKg > 0 ? (int)((long)grossKg * 1000 / axleLimitKg) : 0;
        bool refused = axlePm > 1300, over = axlePm > 1000;
        long cap = baseCmps <= 0 ? 0 : Math.Min(1000, (long)gaitCapCmps * 1000 / baseCmps);
        long speed = (long)baseCmps * f / 1000 * cap / 1000;
        if (over) speed = speed * 8 / 10;
        if (ice) speed = speed * 7 / 10;
        return new SpeedResult(freq, pullN, ratio, f, refused ? 0 : (int)speed, axlePm, f == 0 || refused, refused, over);
    }

    /// <summary>Speed of a loaded vehicle on a surface and grade, the call the live cart and the abstract caravan share.</summary>
    public static SpeedResult OnSurface(VehicleTuning tu, VehicleStats v, int cargoKg, int peopleKg, Surface s, int slopePm, long pullN, int gaitCapCmps, int windPm = 0)
    {
        int resist = tu.Crr(s, v.Runners) + slopePm + windPm;
        return Evaluate(v.BaseSpeedCmps, v.MassKg + cargoKg, v.MassKg + cargoKg + peopleKg, resist, pullN, gaitCapCmps, v.AxleLimitKg, s == Surface.Ice);
    }

    // ---------------------------------------------------------------- the abstract tier's side

    /// <summary>
    /// A caravan leg's travel speed in millimetres per game second on a surface and grade: the same <see cref="OnSurface"/> the live cart uses, for the templates the Phase 2 caravans are
    /// (cart and ox wagon, loaded to capacity with their teams), so a leg on dry level road is exactly the Phase 2 number (125 and 90 mm/gs).
    /// </summary>
    public static int CaravanMmPerGs(VehicleTuning tu, Caravans.VehicleKind kind, Surface s, int slopePm, int loadKg = -1)
    {
        var (v, teamPull, cap) = CaravanTemplate(kind);
        int cargo = loadKg >= 0 ? loadKg : Caravans.Vehicles.CapKg(kind);
        return MmPerGs(OnSurface(tu, v, cargo, 0, s, slopePm, teamPull, cap).SpeedCmps);
    }

    /// <summary>The seconds a leg of <paramref name="lengthMm"/> takes at <paramref name="mmPerGs"/>; 0 speed is a leg that cannot be walked (the caller delays it, spec 4.6).</summary>
    public static long LegSeconds(long lengthMm, int mmPerGs) => mmPerGs <= 0 ? long.MaxValue : (lengthMm + mmPerGs - 1) / mmPerGs;

    private static (VehicleStats V, long Pull, int GaitCap) CaravanTemplate(Caravans.VehicleKind k) => k switch
    {
        Caravans.VehicleKind.Cart => (CartStats, 530, 500),            // a palfrey, trot 5.0 m/s
        Caravans.VehicleKind.OxWagon => (OxWagonStats, 1300, 180),     // two oxen, walk 1.8 m/s
        _ => (PackStats, 560, 450),
    };

    // the Phase 2 vehicles' own numbers (kept equal to the derived stats of the templates by test H-13)
    internal static readonly VehicleStats CartStats = new() { MassKg = 165, CargoKg = 500, CargoL = 1000, BaseSpeedCmps = 250, AxleLimitKg = 700, HitchSlots = 1, Driver = 1 };
    internal static readonly VehicleStats OxWagonStats = new() { MassKg = 300, CargoKg = 1000, CargoL = 2000, BaseSpeedCmps = 180, AxleLimitKg = 1500, HitchSlots = 2, Driver = 1 };
    internal static readonly VehicleStats PackStats = new() { MassKg = 6, CargoKg = 100, CargoL = 250, BaseSpeedCmps = 300, AxleLimitKg = 0, HitchSlots = 0 };
}
