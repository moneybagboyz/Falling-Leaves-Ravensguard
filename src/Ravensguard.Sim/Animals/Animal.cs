using Ravensguard.Sim.Core;

namespace Ravensguard.Sim.Animals;

public enum AnimalState : byte { Free, Tied, Ridden, Hitched, Following, Stabled, Dead }

/// <summary>
/// One individual animal (horses-and-carts spec 3.1): a row that exists only where an animal needs to be one (ridden, hitched, owned, promoted). Herds stay head counts. Every field is saved.
/// Stamina is kept in thousandths of a stamina point so a metre of trot (2 sp) and a second of rest (+15) both land exactly whatever the game speed.
/// </summary>
public sealed class Animal
{
    public const int Max = 1_000;
    public EntityId Id;
    public int Breed;
    public byte Sex;
    public int BirthDay;
    public EntityId Owner;
    public int MassKg;
    public int TrainedPm = 700;
    public int Temper;
    /// <summary>Short-term stamina, thousandths of a point (0..10,000,000).</summary>
    public long StaminaU = 10_000_000;
    /// <summary>Long-term fatigue, millionths of a permille (see <see cref="WorkFatiguePm"/>).</summary>
    public long FatigueU;
    /// <summary>Food reserve in hundredths of a day (100 = a full day banked); below 100 the animal is weak.</summary>
    public int FeedCenti = 100;
    /// <summary>Water, parts per million (1,000,000 just drunk, 0 after 24 game hours dry).</summary>
    public int WaterPpm = 1_000_000;
    public int ThirstHours;
    public ushort[] Shoes = [1000, 1000, 1000, 1000];
    /// <summary>Shoe wear carried to the next permille (millimetre-tenths of road).</summary>
    public long ShoeWearU;
    public byte Lameness;
    public int LameDays, Lame3Days;
    public long LameHardMm;
    public int HealthPm = 1000;
    public AnimalState State;
    public int Stable = -1;
    public bool Saddled = true;
    public bool Winded;
    public long XMm, YMm;
    public long OdometerMm;
    public long ForcedWindow = long.MinValue;
    /// <summary>The herd it was materialised from (settlement index, -1 when it never was a herd animal) and the vehicle it is hitched to.</summary>
    public int HerdSettlement = -1;
    public EntityId Vehicle;
    public long LastDay = long.MinValue;

    public int StaminaSp => (int)(StaminaU / 1000);
    public int WorkFatiguePm => (int)(FatigueU / 1_000_000);
    public bool Weak => FeedCenti < 100;
    public bool Alive => State != AnimalState.Dead;
    public bool AnyShoeLost { get { foreach (var s in Shoes) if (s == 0) return true; return false; } }
    public int LowestShoe { get { int m = 1000; foreach (var s in Shoes) if (s < m) m = s; return m; } }

    public Animal Clone() { var c = (Animal)MemberwiseClone(); c.Shoes = (ushort[])Shoes.Clone(); return c; }
}

/// <summary>Where a herd's head counts live (farming's herds for oxen, the stable ledger for horses): materialising takes one head, folding gives it back.</summary>
public interface IHerdSource
{
    int Head(int settlement, string species);
    bool Take(int settlement, string species);
    void Give(int settlement, string species);
}

/// <summary>Head counts of the species farming does not keep (horses, mules, donkeys), per settlement; saved with the animals.</summary>
public sealed class HerdLedger : IHerdSource
{
    private readonly SortedDictionary<(int Settlement, string Species), int> _head = [];
    public int Head(int settlement, string species) => _head.TryGetValue((settlement, species), out var n) ? n : 0;
    public bool Take(int settlement, string species) { int n = Head(settlement, species); if (n <= 0) return false; _head[(settlement, species)] = n - 1; return true; }
    public void Give(int settlement, string species) => _head[(settlement, species)] = Head(settlement, species) + 1;
    public void Set(int settlement, string species, int n) => _head[(settlement, species)] = n;
    public IEnumerable<(int Settlement, string Species, int Head)> Rows() { foreach (var (k, v) in _head) yield return (k.Settlement, k.Species, v); }
    public bool IsEmpty => _head.Count == 0;
}

/// <summary>Farming's livestock for oxen and cows and sheep (read and counted down by one when a head is materialised), the ledger for the rest.</summary>
public sealed class LivestockHerds(Farming.LivestockState farm, HerdLedger rest) : IHerdSource
{
    public int Head(int s, string sp) => sp switch { "Ox" => Cell(farm.Oxen, s), "Cow" => Cell(farm.Cows, s), "Sheep" => Cell(farm.Sheep, s), _ => rest.Head(s, sp) };
    public bool Take(int s, string sp)
    {
        switch (sp)
        {
            case "Ox": return Dec(farm.Oxen, s);
            case "Cow": return Dec(farm.Cows, s);
            case "Sheep": return Dec(farm.Sheep, s);
            default: return rest.Take(s, sp);
        }
    }
    public void Give(int s, string sp)
    {
        switch (sp)
        {
            case "Ox": farm.Oxen[s]++; break;
            case "Cow": farm.Cows[s]++; break;
            case "Sheep": farm.Sheep[s]++; break;
            default: rest.Give(s, sp); break;
        }
    }
    private static int Cell(int[] a, int s) => (uint)s < (uint)a.Length ? a[s] : 0;
    private static bool Dec(int[] a, int s) { if ((uint)s >= (uint)a.Length || a[s] <= 0) return false; a[s]--; return true; }
}

/// <summary>The animals that are individuals, in id order; ids come from a private counter (Other kind, serials from 0x700_0000).</summary>
public sealed class AnimalTable
{
    public const ulong SerialBase = 0x700_0000;
    private readonly List<Animal> _rows = [];
    public ulong NextSerial = 1;
    public IReadOnlyList<Animal> Rows => _rows;
    public int Count => _rows.Count;
    public AnimalCatalog Catalog { get; }

    public AnimalTable(AnimalCatalog catalog) { Catalog = catalog; }

    public static EntityId MakeId(ulong serial) => EntityId.MakeIn(IdKind.Other, 0, SerialBase + serial);
    public static bool IsAnimalId(EntityId id) => id.Kind == IdKind.Other && (id.Serial & ~0xFFFFFUL) == SerialBase;

    public Animal? Find(EntityId id)
    {
        int lo = 0, hi = _rows.Count - 1;
        while (lo <= hi) { int m = (lo + hi) >> 1; int c = _rows[m].Id.CompareTo(id); if (c == 0) return _rows[m]; if (c < 0) lo = m + 1; else hi = m - 1; }
        return null;
    }

    /// <summary>A new animal of a breed, trained as given, at full health and stamina, owned by <paramref name="owner"/>.</summary>
    public Animal Create(Breed b, EntityId owner, int trainedPm = 700, int temper = 0, long xMm = 0, long yMm = 0, int birthDay = 0)
    {
        var a = new Animal { Id = MakeId(NextSerial++), Breed = b.Index, Owner = owner, MassKg = b.MassKg, TrainedPm = trainedPm, Temper = temper, StaminaU = b.StaminaSp * 1000L, XMm = xMm, YMm = yMm, BirthDay = birthDay };
        _rows.Add(a);
        return a;
    }

    /// <summary>
    /// Takes one head of the species out of a herd and makes it a row (spec 3.1: <c>Head -= 1</c>), its temper and training drawn from stream 0x352 keyed on the settlement and the row's serial,
    /// so the same herd gives the same animal at any game speed. Null when the herd has none.
    /// </summary>
    public Animal? Materialise(IHerdSource herds, int settlement, Breed b, ulong seed, EntityId owner, long xMm = 0, long yMm = 0)
    {
        if (!herds.Take(settlement, b.Species)) return null;
        ulong serial = NextSerial;
        var a = Create(b, owner, 400 + DetRng.Range(seed, 0, (ulong)settlement, RngStreams.AnimalBreed, (uint)(serial * 2), 0, 400), DetRng.Range(seed, 0, (ulong)settlement, RngStreams.AnimalBreed, (uint)(serial * 2 + 1), -60, 61), xMm, yMm);
        a.HerdSettlement = settlement;
        return a;
    }

    /// <summary>
    /// Gives an animal back to its herd (<c>Head += 1</c>) and removes the row, when it is still a herd animal: it came from a herd, belongs to no one and stands free. False (the row stays) otherwise.
    /// </summary>
    public bool Fold(IHerdSource herds, Animal a)
    {
        if (a.HerdSettlement < 0 || a.Owner != EntityId.None || a.State != AnimalState.Free || !a.Alive) return false;
        herds.Give(a.HerdSettlement, Catalog.At(a.Breed).Species);
        _rows.Remove(a);
        return true;
    }

    /// <summary>Removes a dead animal that was never a herd animal, or any row the caller is done with (sold, eaten).</summary>
    public void Remove(Animal a) => _rows.Remove(a);

    /// <summary>Rows that were saved, in id order (restore).</summary>
    public void Restore(IEnumerable<Animal> rows, ulong nextSerial) { _rows.Clear(); _rows.AddRange(rows.OrderBy(r => r.Id)); NextSerial = nextSerial; }
}
