using Ravensguard.Content;
using Ravensguard.Sim.Core;

namespace Ravensguard.Sim.Carts;

/// <summary>The water numbers (vehicle-designer 3.4), read from content (<c>vehicles/water.jsonc</c>); integers in mm, mm/s and newtons.</summary>
public sealed class WaterTuning
{
    public (string Id, int MaxWidthCm, int DepthMm, int CurrentMmps)[] Classes = [("brook", 300, 400, 400), ("stream", 1000, 900, 600), ("river", 4000, 1800, 800), ("great_river", 0, 3000, 1000)];
    public int FordDepthMm = 500, HighDayDepthMm = 300, HighDayCurrentMmps = 300, HighDaysCap = 3, DragK = 8, RowerN = 70, PolerN = 120, PolingMaxDepthMm = 3000, KeelClearanceMm = 150, TenderGmMm = 150;
    public int BeamSeaWindMps = 10, CapsizePm = 20, WaveFreeboardMm = 100, SwampLps = 20, BailLps = 5, FerryMmps = 500, FerryFeePerson = 15, FerryFeeCart = 40, FishKgPerCrewDay = 12, FishWinterPm = 400, FishShoreKgDay = 4;

    /// <summary>The shipped numbers, for the maths that has no content database.</summary>
    public static readonly WaterTuning Default = new();

    public static WaterTuning From(ContentDatabase db)
    {
        var d = db.All<VehicleWaterDef>().OrderBy(x => x.Id, StringComparer.Ordinal).FirstOrDefault();
        if (d == null) return Default;
        var t = new WaterTuning
        {
            FordDepthMm = d.FordDepthMm, HighDayDepthMm = d.HighDayDepthMm, HighDayCurrentMmps = d.HighDayCurrentMmps, HighDaysCap = d.HighDaysCap, DragK = d.DragK, RowerN = d.RowerN, PolerN = d.PolerN,
            PolingMaxDepthMm = d.PolingMaxDepthMm, KeelClearanceMm = d.KeelClearanceMm, TenderGmMm = d.TenderGmMm, BeamSeaWindMps = d.BeamSeaWindMps, CapsizePm = d.CapsizePm, WaveFreeboardMm = d.WaveFreeboardMm,
            SwampLps = d.SwampLps, BailLps = d.BailLps, FerryMmps = d.FerryMmps, FerryFeePerson = d.FerryFeePerson, FerryFeeCart = d.FerryFeeCart, FishKgPerCrewDay = d.FishKgPerCrewDay, FishWinterPm = d.FishWinterPm,
            FishShoreKgDay = d.FishShoreKgDay,
        };
        if (d.Classes.Count > 0) t.Classes = [.. d.Classes.Select(c => (c.Id, c.MaxWidthCm, c.DepthMm, c.CurrentMmps))];
        return t;
    }

    /// <summary>The width class of a river <paramref name="widthCm"/> wide (vehicle-designer 3.4, open question 2: by width class, not measured).</summary>
    public (string Id, int DepthMm, int CurrentMmps) ClassOf(int widthCm)
    {
        foreach (var c in Classes) if (c.MaxWidthCm == 0 || widthCm < c.MaxWidthCm) return (c.Id, c.DepthMm, c.CurrentMmps);
        var last = Classes[^1]; return (last.Id, last.DepthMm, last.CurrentMmps);
    }

    /// <summary>Depth and current of a river of this width after <paramref name="highDays"/> river-high days (+300 mm and +0.3 m/s a day, at most 3 days).</summary>
    public (int DepthMm, int CurrentMmps) River(int widthCm, int highDays)
    {
        var c = ClassOf(widthCm); int h = Math.Clamp(highDays, 0, HighDaysCap);
        return (c.DepthMm + h * HighDayDepthMm, c.CurrentMmps + h * HighDayCurrentMmps);
    }

    /// <summary>A ford's depth after river-high days (500 mm, +300 a day).</summary>
    public int FordDepth(int highDays) => FordDepthMm + Math.Clamp(highDays, 0, HighDaysCap) * HighDayDepthMm;
}

/// <summary>How a boat floats with a load (vehicle-designer 4.5): total mass, draft, freeboard, the stability terms and GM (all mm).</summary>
public readonly record struct BoatLoad(int TotalKg, int DraftMm, int FreeboardMm, int KbMm, int BmMm, int KgMm, int GmMm)
{
    public bool Swamped => FreeboardMm < 0;
    public BoatStability Stability(int tenderGmMm) => GmMm <= 0 ? BoatStability.Capsizes : GmMm < tenderGmMm ? BoatStability.Tender : BoatStability.Steady;
}

public enum BoatStability : byte { Steady, Tender, Capsizes }

/// <summary>
/// River boats (vehicle-designer 4.5): displacement and draft from the hull's waterplane against the mass, the metacentric height from the hull's beam and the mass's height, the speed of rowers, polers,
/// a towing team or a sail against water drag, the current along the river, the depth a boat needs, the capsize and swamp rules, ferries and fishing boats. Integer millimetres, newtons and mm/s only;
/// <see cref="IntMath.Isqrt"/> for the square root.
/// </summary>
public static class BoatMath
{
    /// <summary>The mass of a person aboard (kg) and where they sit or stand above the keel (mm).</summary>
    public const int PersonKg = 75, SeatedMm = 600, StandingMm = 1000;

    /// <summary><c>A_wp = sum over hull cells of 1 m2 x waterplane_pm</c>, mm2.</summary>
    public static long WaterplaneOf(int hullCells, int waterplanePm) => (long)hullCells * 1_000_000L * waterplanePm / 1000;

    /// <summary><c>draft_mm = mass_kg x 10^9 / (A_wp_mm2 x block_pm)</c> (64-bit, nearest).</summary>
    public static int DraftMm(long massKg, long waterplaneMm2, int blockPm)
    {
        if (waterplaneMm2 <= 0 || blockPm <= 0) return int.MaxValue;
        Int128 den = (Int128)waterplaneMm2 * blockPm;
        Int128 num = (Int128)massKg * 1_000_000_000L;
        return (int)Math.Min(int.MaxValue, (long)((num + den / 2) / den));
    }

    /// <summary>The most a hull may weigh in all to keep <paramref name="freeboardMm"/> above the water: <c>(depth - freeboard) x A_wp x block / 10^9</c>.</summary>
    public static int MaxMassKg(long waterplaneMm2, int blockPm, int hullDepthMm, int freeboardMm) => (int)((Int128)(hullDepthMm - freeboardMm) * waterplaneMm2 * blockPm / 1_000_000_000L);

    /// <summary>KB = draft / 2 (nearest, half up).</summary>
    public static int KbMm(int draftMm) => (draftMm + 1) / 2;

    /// <summary>BM = B_eff2 / (12 x draft x block / 1000), B_eff = beam cells x 1,000 mm x 700 pm (nearest).</summary>
    public static int BmMm(int beamCells, int draftMm, int blockPm)
    {
        if (draftMm <= 0 || blockPm <= 0) return 0;
        long b = (long)beamCells * 700;
        Int128 num = (Int128)b * b * 1000; Int128 den = (Int128)12 * draftMm * blockPm;
        return (int)(long)((num + den / 2) / den);
    }

    /// <summary>The height of the cargo's centre of mass (stacked: 200 mm plus 100 mm per 250 kg a cell).</summary>
    public static int CargoHeightMm(int cargoKg, int cells) => cells <= 0 ? 200 : 200 + (int)((long)cargoKg * 100 / (250L * cells));

    /// <summary>KG: the mass-weighted height of the parts (their moment), the cargo and the people (nearest).</summary>
    public static int KgMm(long partsKg, long partsMomentKgMm, int cargoKg, int cargoHeightMm, int seatedKg, int standingKg)
    {
        long mass = partsKg + cargoKg + seatedKg + standingKg; if (mass <= 0) return 0;
        long mom = partsMomentKgMm + (long)cargoKg * cargoHeightMm + (long)seatedKg * SeatedMm + (long)standingKg * StandingMm;
        return (int)((mom + mass / 2) / mass);
    }

    /// <summary>The floating of a hull: draft, freeboard, KB, BM, KG and GM = KB + BM - KG.</summary>
    public static BoatLoad Float(long waterplaneMm2, int blockPm, int hullDepthMm, int beamCells, int totalKg, int kgMm)
    {
        int draft = DraftMm(totalKg, waterplaneMm2, blockPm), kb = KbMm(draft), bm = BmMm(beamCells, draft, blockPm);
        return new BoatLoad(totalKg, draft, hullDepthMm - draft, kb, bm, kgMm, kb + bm - kgMm);
    }

    /// <summary>A designed boat with cargo and people seated (the designer's stats pane and the live boat).</summary>
    public static BoatLoad Load(VehicleStats st, int cargoKg, int seatedKg, WaterTuning wt, int standingKg = 0)
    {
        int kg = KgMm(st.MassKg, st.MomentKgMm, cargoKg, CargoHeightMm(cargoKg, Math.Max(1, st.HullCells)), seatedKg, standingKg);
        return Float(st.WaterplaneMm2, st.BlockPm, st.HullDepthMm, st.BeamCells, st.MassKg + cargoKg + seatedKg + standingKg, kg);
    }

    /// <summary>The wetted area, mm2: <c>A_wp + perimeter_m x draft_m</c>.</summary>
    public static long WetMm2(long waterplaneMm2, int perimeterM, int draftMm) => waterplaneMm2 + (long)perimeterM * 1000 * draftMm;

    /// <summary>Water speed (mm/s) under a force: <c>v = sqrt(F / (drag_k x A_wet))</c>; in mm/s, <c>isqrt(F x 10^12 / (k x A_wet_mm2))</c>.</summary>
    public static int WaterSpeedMmps(long forceN, int dragK, long wetMm2)
    {
        if (forceN <= 0 || wetMm2 <= 0 || dragK <= 0) return 0;
        Int128 q = (Int128)forceN * 1_000_000_000_000L / ((Int128)dragK * wetMm2);
        return (int)IntMath.Isqrt(q > long.MaxValue ? long.MaxValue : (long)q);
    }

    /// <summary>The square sail's push: <c>0.6 x sail_m2 x wind2 x cos(angle)</c> N, for winds within 90 degrees of astern (cos as permille).</summary>
    public static long SailN(int sailM2, int windMps, int cosPm) => cosPm <= 0 ? 0 : (long)6 * sailM2 * windMps * windMps * cosPm / 10_000;

    /// <summary>
    /// The power a boat has (newtons): rowers at 70 N each (x3 on a hard stroke), polers at 120 N each where the water is no deeper than 3 m, a towing team on a towpath, a sail. The team's pull comes
    /// from the Phase 3 team rules (<see cref="VehicleTable.Team"/>).
    /// </summary>
    public static long PowerN(WaterTuning wt, int rowers, int polers, int depthMm, long towN, long sailN, bool hardStroke = false)
    {
        long f = (long)rowers * wt.RowerN * (hardStroke ? 3 : 1);
        if (depthMm <= wt.PolingMaxDepthMm) f += (long)polers * wt.PolerN;
        return f + towN + sailN;
    }

    /// <summary>Ground speed along the river: water speed plus the current downstream, minus it upstream; with no power the boat drifts at the current (never upstream).</summary>
    public static int GroundMmps(int waterMmps, int currentMmps, bool downstream) => waterMmps <= 0 ? (downstream ? currentMmps : 0) : downstream ? waterMmps + currentMmps : Math.Max(0, waterMmps - currentMmps);

    /// <summary>A boat can enter water at least <c>draft + 150 mm</c> deep; shallower, it is aground.</summary>
    public static bool Floats(int draftMm, int depthMm, WaterTuning wt) => draftMm + wt.KeelClearanceMm <= depthMm;

    /// <summary>
    /// One 20-game-second check of a tender boat in beam seas (vehicle-designer 4.5): GM at or under 0 capsizes at once; under the tender line (150 mm) in a cross wind of 10 m/s or more it capsizes with
    /// 20 pm, stream 0x550 keyed on <c>GameSec / 20</c> and the boat. A steady boat never rolls.
    /// </summary>
    public static bool CapsizeCheck(ulong seed, EntityId boat, long gameSec, int gmMm, int crossWindMps, WaterTuning wt)
    {
        if (gmMm <= 0) return true;
        if (gmMm >= wt.TenderGmMm || crossWindMps < wt.BeamSeaWindMps) return false;
        return DetRng.Range(seed, gameSec / 20, boat.Value, RngStreams.BoatCapsize, 0, 0, 1000) < wt.CapsizePm;
    }

    /// <summary>
    /// Water a boat takes in over a 20-second check (litres): swamped (freeboard under 0) it sinks; in waves (10 m/s on a great river) with freeboard under 100 mm, 20 L a second per low or broken cell,
    /// jittered by up to a tenth (stream 0x551), less 5 L a second per person bailing.
    /// </summary>
    public static int WaterInL(ulong seed, EntityId boat, long gameSec, int freeboardMm, int windMps, bool greatRiver, int brokenCells, int bailers, WaterTuning wt)
    {
        int cells = brokenCells + (greatRiver && windMps >= wt.BeamSeaWindMps && freeboardMm < wt.WaveFreeboardMm ? 1 : 0);
        if (cells == 0) return 0;
        int jitter = DetRng.Range(seed, gameSec / 20, boat.Value, RngStreams.BoatSwampJitter, 0, 900, 1101);
        long inL = (long)cells * wt.SwampLps * 20 * jitter / 1000;
        return (int)Math.Max(0, inL - (long)bailers * wt.BailLps * 20);
    }

    // ---------------------------------------------------------------- ferries and fishing boats

    /// <summary>A ferry's crossing of a river <paramref name="widthMm"/> wide on its rope at 0.5 m/s, game seconds (rounded up).</summary>
    public static long FerrySeconds(long widthMm, WaterTuning wt) => (widthMm + wt.FerryMmps - 1) / wt.FerryMmps;

    /// <summary>The ferryman's fee (Phase 3's 15 mites a person or animal, 40 a cart).</summary>
    public static long FerryFee(int people, int animals, int carts, WaterTuning wt) => (long)(people + animals) * wt.FerryFeePerson + (long)carts * wt.FerryFeeCart;

    /// <summary>A fishing boat's catch a day, grams: 12 kg per crew member (x0.4 in winter), from the lake or river stock; the shore rate is 4 kg.</summary>
    public static long FishGramsPerDay(int crew, bool winter, WaterTuning wt) => (long)crew * wt.FishKgPerCrewDay * 1000 * (winter ? wt.FishWinterPm : 1000) / 1000;

    // ---------------------------------------------------------------- abstract legs

    /// <summary>A barge leg's travel time, game seconds: its length at the ground speed (rounded up), and the abstract delay of 0 to 2 hours drawn from 0x552 (key barge id, leg) when asked.</summary>
    public static long LegSeconds(long lengthMm, int groundMmps) => groundMmps <= 0 ? long.MaxValue : (lengthMm + groundMmps - 1) / groundMmps;

    public static int LegDelaySeconds(ulong seed, EntityId barge, int leg) => DetRng.Range(seed, 0, barge.Value, RngStreams.BargeLegDelay, (uint)leg, 0, 7201);
}
