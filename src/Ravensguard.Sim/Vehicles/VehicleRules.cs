using System.Security.Cryptography;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Goods;

namespace Ravensguard.Sim.Carts;

/// <summary>Fishing boats (vehicle-designer 4.5): a crew of two extracts 12 kg a day each (winter x0.4) from the lake or river stock into the boat's fish well, never more than the stock holds.</summary>
public static class Fishing
{
    /// <summary>One day's catch into the boat's store: grams taken from <paramref name="stockG"/> (the Phase 2 resource row's stock), returned.</summary>
    public static long Day(VehicleRow boat, VehicleTable t, ushort fish, int crew, bool winter, ref long stockG, WaterTuning wt)
    {
        var st = t.StatsOf(boat);
        if (!st.Boat || boat.Water is not (WaterState.Afloat or WaterState.Aground) || crew <= 0) return 0;
        long want = Math.Min(BoatMath.FishGramsPerDay(Math.Min(crew, Math.Max(1, st.Rowers)), winter, wt), Math.Max(0, stockG));
        long got = want <= 0 ? 0 : boat.Cargo.CreateUpTo(fish, want, CreateReason.Harvest);
        stockG -= got;
        return got;
    }
}

/// <summary>What a ferry crossing came to.</summary>
public readonly record struct FerryCrossing(bool Paid, long Seconds, long Fee);

/// <summary>
/// Ferries (vehicle-designer 4.5): a ferry with ramps at a ford or a crossing with none, hauled along its rope at 0.5 m/s by a ferryman; Phase 3's fee of 15 mites a person or animal and 40 a cart goes to
/// him through the ledger. Carts drive on and off over the ramps (the ramp cells are portals of the ferry's deck to the bank).
/// </summary>
public static class Ferry
{
    public static FerryCrossing Cross(Ledger ledger, PurseId payer, PurseId ferryman, long widthMm, int people, int animals, int carts, WaterTuning wt)
    {
        long fee = BoatMath.FerryFee(people, animals, carts, wt);
        if (fee > 0 && !ledger.Transfer(payer, ferryman, new Coin(fee), TransferReason.Toll)) return new FerryCrossing(false, 0, fee);
        return new FerryCrossing(true, BoatMath.FerrySeconds(widthMm, wt), fee);
    }

    /// <summary>The ramp cells of a design (deck 0): where its deck meets the bank.</summary>
    public static List<(int X, int Y)> Ramps(VehicleBlueprint bp, VehicleCatalog cat) =>
        [.. bp.Parts.Where(p => p.Deck == 0 && cat.PartList[p.Part].Tags.Contains("ramp")).Select(p => ((int)p.X, (int)p.Y)).Distinct()];
}

/// <summary>
/// NPC drivers (vehicle-designer 4.4): they keep 5% further below the stuck ratio than the hero (they ease off, or wait for help) and avoid deep mud when they can (a route costs three times as much
/// on any surface that can bog a wheel). The same numbers as the hero's, only more careful.
/// </summary>
public static class NpcDriving
{
    /// <summary>The pull ratio an NPC driver keeps to on a bogging surface: 5% under the hero's safe ratio (700 -> 650).</summary>
    public static int MaxRatioPm => Stuck.SafeRatioPm - 50;

    /// <summary>The route cost of a cell, permille: x3 where the wheels can bog (resistance 170 and over).</summary>
    public static int RouteCostPm(VehicleTuning tu, Surface s, bool runners) => Stuck.CanBog(tu, s, runners) ? 3000 : 1000;

    /// <summary>
    /// Whether an NPC driver goes on over this ground with this load and team (else he stops: unloads, waits for a second team, or goes round). Their skill (Riding or Husbandry) only changes how close
    /// to the line they dare: none of them go past it.
    /// </summary>
    public static bool WillDrive(int ratioPm, VehicleTuning tu, Surface s, bool runners) => !Stuck.CanBog(tu, s, runners) || ratioPm <= MaxRatioPm;
}

/// <summary>The hash trail's <c>VehicleHash</c> (overview 8.9): the vehicle rows with their parts, the sites, commissions and voyages; and the golden of VD-11 to VD-16 (VD-20).</summary>
public static class VehicleHash
{
    public static string Of(Animals.RideBook b)
    {
        using var h = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        void L(long v) { Span<byte> s = stackalloc byte[8]; BitConverter.TryWriteBytes(s, v); h.AppendData(s); }
        foreach (var v in b.Vehicles.Rows)
        {
            L((long)v.Id.Value); L(v.XMm); L(v.YMm); L(v.OdometerMm); L(v.WearAccMm); L(v.Stuck ? 1 : 0); L((long)v.Water); L(v.WaterInL);
            h.AppendData(System.Text.Encoding.UTF8.GetBytes(v.Design));
            foreach (var p in v.Parts) { L(p.SortKey); L(p.Part); L((long)p.State); L((long)p.Tier); L(p.HpMilli); L(p.WorkMilli); }
        }
        foreach (var s in b.Works.Sites.Values) { L((long)s.Vehicle.Value); L(s.WorkedMilli); L(s.DoneSec); foreach (var c in s.Crew) L((long)c.Value); }
        foreach (var c in b.Works.Commissions.Values) { L((long)c.Vehicle.Value); L((long)c.State); L(c.PartsMites); L(c.InstallMites); }
        foreach (var y in b.Works.Voyages.Values) { L((long)y.Boat.Value); L(y.AtMm); L(y.ToMm); L(y.GroundMmps); L(y.LegStartSec); L(y.ArrivedSec); }
        return Convert.ToHexString(h.GetHashAndReset())[..16];
    }

    /// <summary>
    /// The live hash trail's part (<c>LiveStateHash.Compute</c>): only what the live tier itself writes, the vehicle the hero drives (its place, facing, odometer, wear, being stuck, water and
    /// parts), read under the ride book's lock; 0 when he drives none. The other rows, the sites, the commissions and the voyages are the tick worker's (an hour's barrier lands at no fixed
    /// live tick, and a replay runs without the worker), so they stay out of the trail (P5-I review, finding 1: hashing them made every replay with a site or a voyage diverge, and the main
    /// thread read them while the worker changed them).
    /// </summary>
    public static ulong Live(Animals.RideBook b)
    {
        lock (b.Gate)
        {
            if (b.DrivenVehicle is not { } v) return 0;
            using var h = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            void L(long x) { Span<byte> s = stackalloc byte[8]; BitConverter.TryWriteBytes(s, x); h.AppendData(s); }
            L((long)v.Id.Value); L(v.XMm); L(v.YMm); L(v.Facing); L(v.OdometerMm); L(v.WearAccMm); L(v.Stuck ? 1 : 0); L((long)v.Water); L(v.WaterInL);
            foreach (var p in v.Parts) { L(p.SortKey); L(p.Part); L((long)p.State); L(p.HpMilli); }
            return BitConverter.ToUInt64(h.GetHashAndReset(), 0);
        }
    }

    /// <summary>VD-20: the numbers of VD-11 to VD-16 in one hash (draft, speeds, depth, the capsize rolls, the swamp water, the fishing, the ferry), the same on every machine.</summary>
    public static string Of(ulong seed)
    {
        var wt = WaterTuning.Default; var boat = EntityId.MakeIn(IdKind.Other, 0, 0x720_0001);
        using var h = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        void L(long v) { Span<byte> s = stackalloc byte[8]; BitConverter.TryWriteBytes(s, v); h.AppendData(s); }
        long awp = BoatMath.WaterplaneOf(30, 900);
        foreach (int kg in new[] { 3000, 15000 })
        {
            int d = BoatMath.DraftMm(kg, awp, 850); L(d);
            L(BoatMath.WaterSpeedMmps(780, wt.DragK, BoatMath.WetMm2(awp, 26, d)));
            L(BoatMath.Floats(d, wt.River(500, 0).DepthMm, wt) ? 1 : 0); L(BoatMath.Floats(d, wt.FordDepth(0), wt) ? 1 : 0); L(BoatMath.Floats(d, wt.FordDepth(2), wt) ? 1 : 0);
        }
        for (long t = 0; t < 600; t += 20) L(BoatMath.CapsizeCheck(seed, boat, 3600 + t, 100, 11, wt) ? 1 : 0);
        for (long t = 0; t < 600; t += 20) L(BoatMath.WaterInL(seed, boat, t, 50, 12, true, 1, 1, wt));
        L(BoatMath.FishGramsPerDay(2, false, wt)); L(BoatMath.FishGramsPerDay(2, true, wt));
        L(BoatMath.FerrySeconds(30_000, wt)); L(BoatMath.FerryFee(1, 1, 1, wt));
        for (int leg = 0; leg < 8; leg++) L(BoatMath.LegDelaySeconds(seed, boat, leg));
        return Convert.ToHexString(h.GetHashAndReset())[..16];
    }
}
