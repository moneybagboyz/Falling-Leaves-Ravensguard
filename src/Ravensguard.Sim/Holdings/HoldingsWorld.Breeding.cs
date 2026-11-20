using Ravensguard.Sim.Animals;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Production;
using RegionData = Ravensguard.Sim.WorldGen.Region.Region;

namespace Ravensguard.Sim.Holdings;

// P6-B6, holdings 3.7 (horse breeding and studs) and 3.8 (coppice): the once-a-year and the daily parts of the holdings world.
public sealed partial class HoldingsWorld
{
    public const byte Mare = 1, Stallion = 2;
    /// <summary>Animals "kept together": within 30 m of each other, fed (spec 3.7: a paddock or a stable with fodder).</summary>
    public const long TogetherMm = 30_000;
    /// <summary>Coppice regrows in 7 game years.</summary>
    public const int CoppiceYears = 7;

    /// <summary>The animals the holdings breed (the hero's riding book); null where the world has none.</summary>
    public Func<RideBook?> Rides { get; set; } = () => null;

    // ------------------------------------------------------------------ breeding

    /// <summary>An animal's sex: the row's own when set (foals), else drawn once from its id (stream 0x6E1, draw 9), so a herd animal is a mare or a stallion for good without the row changing.</summary>
    public byte SexOf(Animal a) => a.Sex != 0 ? a.Sex : DetRng.Range(Seed, 0, a.Id.Value, RngStreams.HorseBreeding, 9, 0, 2) == 0 ? Mare : Stallion;

    /// <summary>The age of an animal in game days (a row with no birth day is a grown one).</summary>
    public static int AgeDays(Animal a, int day) => a.BirthDay <= 0 ? int.MaxValue / 2 : Math.Max(0, day - a.BirthDay);

    /// <summary>Whether an animal is old enough to be ridden or worked (the breed's maturity, 3 game years).</summary>
    public static bool Mature(Animal a, Breed b, int day) => AgeDays(a, day) >= b.Def.MaturityYears * TimeConst.DaysPerYear;

    /// <summary>The price of an animal by age (spec 3.7: sold at any age): a foal a third of the breed's price, a yearling half, a two-year-old four fifths, a grown animal the price.</summary>
    public static long PriceMites(Breed b, int ageDays)
    {
        int year = ageDays / TimeConst.DaysPerYear;
        int pm = year switch { 0 => 330, 1 => 500, 2 => 800, _ => 1000 };
        return b.Def.PriceMites * pm / 1000;
    }

    private static bool KeptOk(Animal a) => a.Alive && a.State is AnimalState.Free or AnimalState.Tied or AnimalState.Stabled && !a.Weak && a.HealthPm >= 500;

    /// <summary>
    /// Once a game year (day 1 of the year), each grown mare kept with a grown stallion of her species is tried, in id order, with probability <c>foal_pct</c> (60): stream 0x6E1 keyed on the mare and
    /// the year. A mare in foal foals <c>gestation_days</c> later (56): the foal takes the mean of its parents' training, temper and mass plus or minus 5 (draws 1 to 3) and a sex (draw 4).
    /// </summary>
    public void BreedingDay(int day)
    {
        var rides = Rides(); if (rides == null) return;
        lock (rides.Gate)
        {
            if (Book.Foaling.Count > 0) Foal(rides, day);
            if (day % TimeConst.DaysPerYear != 0) return;
            int year = day / TimeConst.DaysPerYear;
            var rows = rides.Animals.Rows;
            foreach (var m in rows.ToList())
            {
                var b = rides.Breeds.At(m.Breed);
                if (b.Def.FoalPct <= 0 || !KeptOk(m) || SexOf(m) != Mare || !Mature(m, b, day)) continue;
                if (Book.Tried.TryGetValue(m.Id.Value, out int last) && last >= year) continue;
                if (Book.Foaling.Any(p => p.Mare == m.Id.Value)) continue;
                Animal? sire = null;
                foreach (var s in rows)
                {
                    if (s == m || SexOf(s) != Stallion || !KeptOk(s) || rides.Breeds.At(s.Breed).Species != b.Species || !Mature(s, rides.Breeds.At(s.Breed), day)) continue;
                    long dx = s.XMm - m.XMm, dy = s.YMm - m.YMm;
                    if (dx * dx + dy * dy > TogetherMm * TogetherMm) continue;
                    sire = s; break;                                                                      // lowest id first: the rows are in id order
                }
                if (sire == null) continue;
                Book.Tried[m.Id.Value] = year;
                if (DetRng.Range(Seed, year, m.Id.Value, RngStreams.HorseBreeding, 0, 0, 100) >= b.Def.FoalPct) continue;
                Book.Foaling.Add(new Pregnancy { Mare = m.Id.Value, Sire = sire.Id.Value, Year = year, DueDay = day + b.Def.GestationDays });
            }
        }
    }

    private void Foal(RideBook rides, int day)
    {
        foreach (var p in Book.Foaling.Where(f => f.DueDay <= day).ToList())
        {
            Book.Foaling.Remove(p);
            var m = rides.Animals.Find(new EntityId(p.Mare)); var s = rides.Animals.Find(new EntityId(p.Sire));
            if (m == null || !m.Alive) continue;
            var b = rides.Breeds.At(m.Breed); var other = s ?? m;
            int Mean(int x, int y, uint draw) => (x + y) / 2 + DetRng.Range(Seed, p.Year, p.Mare, RngStreams.HorseBreeding, draw, -5, 6);
            var f = rides.Animals.Create(b, m.Owner, Math.Clamp(Mean(m.TrainedPm, other.TrainedPm, 1), 0, 1000), Mean(m.Temper, other.Temper, 2), m.XMm, m.YMm, Math.Max(1, day));
            f.MassKg = Math.Max(1, Mean(m.MassKg, other.MassKg, 3));
            f.Sex = DetRng.Range(Seed, p.Year, p.Mare, RngStreams.HorseBreeding, 4, 0, 2) == 0 ? Mare : Stallion;
            f.State = m.State == AnimalState.Stabled ? AnimalState.Stabled : AnimalState.Free; f.Stable = m.Stable; f.Saddled = false;
            Book.FoalsBorn++;
        }
    }

    // ------------------------------------------------------------------ coppice and clear-felling

    /// <summary>
    /// Cuts the coppice of a woodland tile (holdings 3.8): nine tenths of its stock come off as logs and firewood into <paramref name="into"/> (the woodcutter's store); the stools stay, and the row
    /// stands full again 7 game years later (<see cref="CoppiceDay"/>). A tile already cut and not yet regrown yields nothing.
    /// </summary>
    public (long LogsG, long FirewoodG) CoppiceCut(int tile, int day, Ravensguard.Sim.Goods.Store into)
    {
        int row = Ew.Resources.Find(tile, ResourceKind.Wood);
        if (row < 0 || Ew.Resources.IsCleared(row) || Book.Coppice.Any(c => c.Tile == tile)) return (0, 0);
        long g = Ew.Resources.Take(row, Ew.Resources.StockG(row) * 9 / 10, day);
        if (g <= 0) return (0, 0);
        long fw = g * Land.GrowthTuning.AssartFirewoodPct / 100, logs = g - fw;
        if (fw > 0) into.Create(Econ.Food.Firewood, fw, Ravensguard.Sim.Goods.CreateReason.Cut);
        if (logs > 0) into.Create(Econ.Goods.IdOf("logs"), logs, Ravensguard.Sim.Goods.CreateReason.Cut);
        Book.Coppice.Add(new CoppiceCut { Tile = tile, CutDay = day, DueDay = day + CoppiceYears * TimeConst.DaysPerYear });
        return (logs, fw);
    }

    /// <summary>The coppice that is due stands full again (the cut's row refills to its cap).</summary>
    public void CoppiceDay(int day)
    {
        if (Book.Coppice.Count == 0) return;
        foreach (var c in Book.Coppice.Where(x => x.DueDay <= day).ToList())
        {
            Book.Coppice.Remove(c);
            int row = Ew.Resources.Find(c.Tile, ResourceKind.Wood);
            if (row >= 0 && !Ew.Resources.IsCleared(row)) Ew.Resources.SetStock(row, Ew.Resources.CapG(row));
        }
    }

    // ------------------------------------------------------------------ a quarry's carting

    /// <summary>The carting a quarry's output pays to reach its market (holdings 3.8): 2 mites per tonne-kilometre, rounded up, for <paramref name="grams"/> over <paramref name="metres"/> of road.</summary>
    public static long CartingMites(long grams, long metres) => (grams * metres * 2 + 1_000_000_000L - 1) / 1_000_000_000L;

    /// <summary>The nearest settlement with a market to a tile and the distance to it in metres (the carting a quarry's price adds); (-1, 0) when the region has none.</summary>
    public (int Settlement, long Metres) NearestMarket(int tile)
    {
        int N = RegionData.Size, best = -1; long bd = long.MaxValue;
        int x = tile % N * RegionData.TileM + RegionData.TileM / 2, y = tile / N * RegionData.TileM + RegionData.TileM / 2;
        for (int i = 0; i < Region.Settlements.Count; i++)
        {
            if (Region.Settlements[i].Class == Ravensguard.Sim.WorldGen.Region.SettlementClass.Farmstead) continue;
            long dx = Region.Settlements[i].CenterX - x, dy = Region.Settlements[i].CenterY - y, d2 = dx * dx + dy * dy;
            if (d2 < bd) { bd = d2; best = i; }
        }
        return best < 0 ? (-1, 0) : (best, IntMath.Isqrt(bd));
    }
}
