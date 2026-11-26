using Ravensguard.Sim.Carts;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.WorldGen.Region;

namespace Ravensguard.Sim.Naval;

/// <summary>A landing place (3.2): a shore tile of a region, sand (a ship runs onto it) or rock (it breaks one).</summary>
public readonly record struct BeachTile(int Tile, ShoreKind Kind);

/// <summary>
/// Beaches and harbour sites, pure from a region's tiles (3.2: derived at load, not saved, so no generated layer changes): a shore tile is a land tile beside a sea tile, sand where it is flat (under 80 permille)
/// and low (under 3 m), rock where it is not.
/// </summary>
public static class Beaches
{
    private static bool IsSea(Region r, int t) => (r.Water[t] & WaterFlags.Sea) != 0;

    public static List<BeachTile> Of(Region r, SeaRules rules)
    {
        var list = new List<BeachTile>(); int S = Region.Size;
        for (int t = 0; t < Region.Tiles; t++)
        {
            if (IsSea(r, t)) continue;
            int x = t % S, y = t / S; bool shore = false;
            if (x > 0 && IsSea(r, t - 1)) shore = true; else if (x < S - 1 && IsSea(r, t + 1)) shore = true; else if (y > 0 && IsSea(r, t - S)) shore = true; else if (y < S - 1 && IsSea(r, t + S)) shore = true;
            if (!shore) continue;
            list.Add(new BeachTile(t, r.SlopePermille[t] < rules.D.BeachSlopePm && r.HeightCm[t] < rules.D.BeachMaxHeightCm ? ShoreKind.Sand : ShoreKind.Rock));
        }
        return list;
    }

    /// <summary>The sand beaches within <paramref name="radiusM"/> of a point (region metres), nearest first, ties by tile: the landing places for a village (2 km).</summary>
    public static List<BeachTile> Near(IEnumerable<BeachTile> all, int xM, int yM, int radiusM)
    {
        int S = Region.Size, T = Region.TileM;
        return [.. all.Where(b => b.Kind == ShoreKind.Sand && D2(b.Tile, S, T, xM, yM) <= (long)radiusM * radiusM).OrderBy(b => D2(b.Tile, S, T, xM, yM)).ThenBy(b => b.Tile)];
    }

    private static long D2(int tile, int S, int T, int xM, int yM) { long dx = (tile % S) * T + T / 2 - xM, dy = (tile / S) * T + T / 2 - yM; return dx * dx + dy * dy; }

    /// <summary>A harbour's mooring sites: shore tiles within 500 m of the settlement, nearest first, kept a ship's length (at least 24 m, so every second tile of 64 m) apart; at most <paramref name="max"/>.</summary>
    public static List<BeachTile> Moorings(IEnumerable<BeachTile> all, int xM, int yM, int max)
    {
        int S = Region.Size, T = Region.TileM; var picked = new List<BeachTile>();
        foreach (var b in all.Where(b => D2(b.Tile, S, T, xM, yM) <= 500L * 500).OrderBy(b => D2(b.Tile, S, T, xM, yM)).ThenBy(b => b.Tile))
        {
            if (picked.Count >= max) break;
            if (picked.Any(p => Math.Abs(p.Tile % S - b.Tile % S) + Math.Abs(p.Tile / S - b.Tile / S) < 2)) continue;
            picked.Add(b);
        }
        return picked;
    }
}

/// <summary>A mooring cell: where a ship lies, and who lies there.</summary>
public sealed class Mooring
{
    public int XMm, YMm;
    public EntityId Ship;
    public bool Free => Ship.Value == 0;
}

/// <summary>What the customs charged and who got it.</summary>
public readonly record struct CustomsResult(bool Paid, long Duty);

/// <summary>
/// A harbour (4.3): moorings (one ship each), a warehouse, porters and perhaps a treadwheel crane. A ship comes in (under oars, or warped at 0.3 m/s by two men on a rope), moors, declares its cargo and pays the
/// customs on its value, and its hold is hauled to the warehouse by the porters and the crane; it leaves when the hold is empty (or not at all if the duty is unpaid) and the mooring is free again.
/// </summary>
public sealed class Harbour
{
    public int Settlement { get; }
    public List<Mooring> Moorings { get; } = [];
    public Store Warehouse { get; }
    public int Porters { get; set; }
    public bool Crane { get; set; }
    private readonly SeaRules _rules; private readonly GoodCatalog _goods;
    private long _carryG;

    public Harbour(int settlement, Store warehouse, GoodCatalog goods, SeaRules rules) { Settlement = settlement; Warehouse = warehouse; _goods = goods; _rules = rules; }

    public Mooring AddMooring(int xMm, int yMm) { var m = new Mooring { XMm = xMm, YMm = yMm }; Moorings.Add(m); return m; }

    public int FreeMoorings => Moorings.Count(m => m.Free);
    public Mooring? Berth(EntityId ship) => Moorings.FirstOrDefault(m => m.Ship == ship);

    /// <summary>Moors a ship in the nearest free cell to where it is (ties to the first), returning it; null when the harbour is full or the ship already lies here.</summary>
    public Mooring? Moor(EntityId ship, long xMm, long yMm)
    {
        if (Berth(ship) != null) return null;
        Mooring? best = null; long bd = long.MaxValue;
        foreach (var m in Moorings)
        {
            if (!m.Free) continue;
            long dx = m.XMm - xMm, dy = m.YMm - yMm, d = dx * dx + dy * dy;
            if (d < bd) { bd = d; best = m; }
        }
        if (best != null) best.Ship = ship;
        return best;
    }

    public bool Release(EntityId ship) { var m = Berth(ship); if (m == null) return false; m.Ship = default; return true; }

    /// <summary>Warping in: seconds for a ship to cross <paramref name="distMm"/> by two men on a rope (0.3 m/s).</summary>
    public long WarpSeconds(long distMm) => Voyages.LegSeconds(distMm, _rules.D.WarpMmps);

    /// <summary>
    /// The customs on a cargo (the value in mites at the harbour's rate, percent of it), paid from the master's purse to the treasury of the realm that holds the town (Phase 6 customs rule). A duty the payer cannot
    /// meet is not paid, and the ship does not unload.
    /// </summary>
    public CustomsResult PayCustoms(Ledger led, PurseId payer, PurseId treasury, long valueMites, int pct = -1)
    {
        long duty = valueMites * (pct < 0 ? _rules.D.CustomsPct : pct) / 100;
        if (duty <= 0) return new CustomsResult(true, 0);
        return new CustomsResult(led.Transfer(payer, treasury, new Coin(duty), TransferReason.Customs), duty);
    }

    /// <summary>Grams a second the harbour can move: porters carry 30 kg a 90 s trip each, a crane lifts 500 kg a 120 s lift.</summary>
    public long GramsPerSecondX1000 => (Porters * (long)_rules.D.PorterKg * 1_000_000L / _rules.D.PorterTripSec) + (Crane ? _rules.D.CraneKg * 1_000_000L / _rules.D.CraneLiftSec : 0);

    /// <summary>
    /// The hauling jobs of <paramref name="seconds"/> seconds: goods move from the ship's hold to the warehouse in good-id order, as many grams as the porters and the crane carry (the remainder carried over),
    /// stopping where the warehouse is full. Returns the grams moved; the goods are real (stores, no creation).
    /// </summary>
    public long Unload(Store hold, int seconds)
    {
        long can = (GramsPerSecondX1000 * seconds + _carryG) / 1000; _carryG = (GramsPerSecondX1000 * seconds + _carryG) % 1000;
        long moved = 0;
        foreach (var (good, qty) in hold.Rows().OrderBy(r => r.Good))
        {
            if (can <= 0) break;
            long unit = Math.Max(1, _goods.MassG(good, 1));
            long n = Math.Min(qty, can / unit); if (n <= 0) continue;
            long got = Store.MoveUpTo(hold, Warehouse, good, n);
            moved += got * unit; can -= got * unit;
            if (got < n) break;
        }
        return moved;
    }

    /// <summary>The ship may leave: its hold is empty and its duty paid.</summary>
    public static bool MayLeave(Store hold, bool dutyPaid) => dutyPaid && hold.TotalMassG() == 0;
}
