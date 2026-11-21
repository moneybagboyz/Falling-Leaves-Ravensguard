using Ravensguard.Sim.Core;

namespace Ravensguard.Sim.Burning;

/// <summary>
/// Firedamp (fire 4.8): in coal seams and deep mines (z at or below -3) some cells hold gas, placed per site by the counter RNG (stream 0x6C2, key site and cell: 10% of a coal seam's cells,
/// 2% of other deep cells). An open flame in a gassy cell blasts it. The tell is honest: a candle or lamp burns taller and blue in a gassy cell (the light's rendering and the HUD line
/// "The flame burns blue"), and a miner with Mining 40 or more names it.
/// </summary>
public static class Firedamp
{
    public const int CoalPm = 100, DeepPm = 20, MinZ = -3, BlastRadiusM = 8, NameSkill = 40;

    /// <summary>Whether the cell holds firedamp (before any blast has vented it): a pure function of the world seed, the site and the cell.</summary>
    public static bool Gassy(ulong seed, ulong site, int x, int y, int z, bool coalSeam)
    {
        if (!coalSeam && z > MinZ) return false;
        int pm = coalSeam ? CoalPm : DeepPm;
        return DetRng.Range(seed, 0, site, RngStreams.Firedamp, (uint)(FireWorld.Key(x, y, z) & 0xFFFFFFFF) ^ (uint)(FireWorld.Key(x, y, z) >> 32), 0, 1000) < pm;
    }

    /// <summary>The blue flame: the flame at (x, y, z) burns blue when its own cell or a neighbour within 2 m holds gas (the warning comes before the blast, which needs the flame in the gas itself).</summary>
    public static bool BlueFlame(ulong seed, ulong site, int x, int y, int z, bool coalSeam, FireBook book)
    {
        for (int dy = -2; dy <= 2; dy++)
            for (int dx = -2; dx <= 2; dx++)
                if (dx * dx + dy * dy <= 4 && !book.Vented.Contains(FireWorld.Key(x + dx, y + dy, z)) && Gassy(seed, site, x + dx, y + dy, z, coalSeam)) return true;
        return false;
    }

    /// <summary>Whether a miner of this Mining skill names the sign (the HUD names it for him).</summary>
    public static bool Names(int miningSkill) => miningSkill >= NameSkill;

    /// <summary>Intensity of the blast at a distance: 3 at the centre (within 2 m), 2 to 5 m, 1 to the edge (8 m); 0 beyond.</summary>
    public static int IntensityAt(int distM) => distM <= 2 ? 3 : distM <= 5 ? 2 : distM <= BlastRadiusM ? 1 : 0;
}

public sealed partial class FireWorld
{
    /// <summary>Crush from a blast: person, momentum in milli-units, game second, the blast's cause (the host turns it into a blow on the wound book).</summary>
    public Action<ulong, long, long, ulong>? Crush { get; set; }
    /// <summary>A blast knocks a flame's carrier down.</summary>
    public Action<ulong, long>? KnockDown { get; set; }
    /// <summary>Cave-in checks on the cells a blast reached (Phase 4 sites): the cell keys within range.</summary>
    public Action<IReadOnlyList<long>, long>? CaveInChecks { get; set; }

    public sealed record BlastReport(int Vented, int Burned, int Crushed, bool CarrierDown, ulong Blast);

    /// <summary>
    /// A flame carried into a gassy cell: the gas blasts, radius 8 m. Burn wounds by distance (3 at the centre, 1 at the edge), a crush by distance, the carrier knocked down, a cave-in check on the
    /// cells in range, timber and thatch in range lit, the vented gas gone for good. Returns what it did, or null when the cell holds no gas (or it was vented already).
    /// </summary>
    public BlastReport? FlameIn(ulong site, int x, int y, int z, long sec, ulong carrier, bool coalSeam)
    {
        long ck = Key(x, y, z);
        if (Book.Vented.Contains(ck) || !Firedamp.Gassy(Seed, site, x, y, z, coalSeam)) return null;
        var parameters = new List<ChronParam> { ChronParam.Count("x", x), ChronParam.Count("y", y) };
        var subjects = carrier != 0 ? new List<EntityId> { new(carrier) } : [];
        var entry = Chronicle?.Invoke(new GameTime(sec), "firedamp_blast", "ch_firedamp_blast", parameters, subjects, [new Cause(CauseKind.Fire, null, "", "ch_note_fire_firedamp")],
            [("x", x.ToString()), ("y", y.ToString()), ("z", z.ToString())]) ?? EntityId.None;
        ulong blast = entry.Value;
        int vented = 0, burned = 0, crushed = 0; bool down = false;
        var reached = new List<long>();
        for (int dz = -1; dz <= 1; dz++)
            for (int dy = -Firedamp.BlastRadiusM; dy <= Firedamp.BlastRadiusM; dy++)
                for (int dx = -Firedamp.BlastRadiusM; dx <= Firedamp.BlastRadiusM; dx++)
                {
                    if (dx * dx + dy * dy > Firedamp.BlastRadiusM * Firedamp.BlastRadiusM) continue;
                    long k = Key(x + dx, y + dy, z + dz);
                    reached.Add(k);
                    if (!Book.Vented.Contains(k) && Firedamp.Gassy(Seed, site, x + dx, y + dy, z + dz, coalSeam)) { Book.Vented.Add(k); vented++; }
                }
        _people.Clear();
        PeopleNear?.Invoke(_people);
        long s = FireClock.SimS(sec);
        foreach (var p in _people.OrderBy(p => p.Person))
        {
            if (Math.Abs(p.Z - z) > 1) continue;
            int d = (int)IntMath.Isqrt((long)(p.X - x) * (p.X - x) + (long)(p.Y - y) * (p.Y - y));
            int inten = Firedamp.IntensityAt(d);
            if (inten == 0) continue;
            BurnAt(p, inten, blast, s, sec); burned++;
            Crush?.Invoke(p.Person, inten * 40_000L, sec, blast); crushed++;
            if (p.Person == carrier) { KnockDown?.Invoke(p.Person, sec); down = true; }
        }
        CaveInChecks?.Invoke(reached, sec);
        // what is flammable within 3 m of the blast takes light
        for (int dy = -3; dy <= 3; dy++)
            for (int dx = -3; dx <= 3; dx++)
                if (dx * dx + dy * dy <= 9) Ignite(x + dx, y + dy, z, sec, FireCause.Firedamp);
        return new BlastReport(vented, burned, crushed, down, blast);
    }
}
