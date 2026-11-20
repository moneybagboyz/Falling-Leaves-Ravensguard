using Ravensguard.Sim.Core;

namespace Ravensguard.Sim.Goods;

/// <summary>
/// A container of goods owned by one record (a household, a granary, a chest, a barn): a small sorted vector of (good, quantity) rows
/// with an optional capacity in mass and stowage volume (items spec 2.2, 2.4, 3.1, 3.5).
/// <para>The only ways stock changes: <see cref="Create"/> (counted faucet), <see cref="Destroy"/> (counted sink), <see cref="Move"/> (conserving) and
/// the spoilage step (a counted sink, reason Spoiled). Quantity never goes negative; every operation is all-or-nothing unless named <c>UpTo</c>.
/// Capacity 0 means unlimited; it is only enforced when <see cref="EnforceCapacity"/> is true (spec: Phase 2 enforces Granary, Barn and chests).</para>
/// </summary>
public sealed class Store
{
    private ushort[] _good = new ushort[4];
    private long[] _qty = new long[4];
    private int _n;
    private readonly GoodCatalog _cat;
    private readonly MassLedger _ledger;

    public StoreKind Kind { get; }
    public EntityId Owner { get; }
    /// <summary>Mass limit in grams (0 = none).</summary>
    public long CapacityMassG { get; }
    /// <summary>Stowage volume limit in millilitres (0 = none).</summary>
    public long CapacityVolumeMl { get; }
    public bool EnforceCapacity { get; }

    public Store(GoodCatalog catalog, MassLedger ledger, StoreKind kind, EntityId owner, long capacityMassG = 0, long capacityVolumeMl = 0, bool? enforce = null)
    {
        _cat = catalog; _ledger = ledger; Kind = kind; Owner = owner; CapacityMassG = capacityMassG; CapacityVolumeMl = capacityVolumeMl;
        EnforceCapacity = enforce ?? kind is StoreKind.Granary or StoreKind.Barn or StoreKind.Chest or StoreKind.Sealed;
    }

    public static bool DefaultEnforced(StoreKind kind) => kind is StoreKind.Granary or StoreKind.Barn or StoreKind.Chest or StoreKind.Sealed;

    /// <summary>The goods catalogue this store counts in.</summary>
    public GoodCatalog Catalog => _cat;
    public int RowCount => _n;
    public ushort GoodAt(int row) => _good[row];
    public long QtyAt(int row) => _qty[row];

    private int Find(ushort good)
    {
        int lo = 0, hi = _n - 1;
        while (lo <= hi)
        {
            int mid = (lo + hi) >> 1;
            if (_good[mid] == good) return mid;
            if (_good[mid] < good) lo = mid + 1; else hi = mid - 1;
        }
        return ~lo;
    }

    public long Qty(ushort good) { int i = Find(good); return i >= 0 ? _qty[i] : 0; }
    public long Qty(string good) => Qty(_cat.IdOf(good));

    public long TotalMassG() { long s = 0; for (int i = 0; i < _n; i++) s += _cat.MassG(_good[i], _qty[i]); return s; }
    public long UsedVolumeMl() { long s = 0; for (int i = 0; i < _n; i++) s += _cat.VolumeMl(_good[i], _qty[i]); return s; }

    private void Set(ushort good, long qty)
    {
        int i = Find(good);
        if (qty == 0)
        {
            if (i >= 0) { Array.Copy(_good, i + 1, _good, i, _n - i - 1); Array.Copy(_qty, i + 1, _qty, i, _n - i - 1); _n--; }
            return;
        }
        if (i >= 0) { _qty[i] = qty; return; }
        i = ~i;
        if (_n == _good.Length) { Array.Resize(ref _good, _n * 2); Array.Resize(ref _qty, _n * 2); }
        Array.Copy(_good, i, _good, i + 1, _n - i); Array.Copy(_qty, i, _qty, i + 1, _n - i);
        _good[i] = good; _qty[i] = qty; _n++;
    }

    /// <summary>How much more of <paramref name="good"/> fits (long.MaxValue when capacity is unlimited or not enforced).</summary>
    public long Room(ushort good)
    {
        if (!EnforceCapacity || (CapacityMassG == 0 && CapacityVolumeMl == 0)) return long.MaxValue;
        long old = Qty(good), room = long.MaxValue;
        if (CapacityMassG > 0)
        {
            long budget = CapacityMassG - (TotalMassG() - _cat.MassG(good, old));
            long maxQty = budget <= 0 ? 0 : _cat.IsPiece(good) ? budget / _cat.Def(good).PieceMassG : budget;
            room = Math.Min(room, Math.Max(0, maxQty - old));
        }
        if (CapacityVolumeMl > 0)
        {
            long budget = CapacityVolumeMl - (UsedVolumeMl() - _cat.VolumeMl(good, old));
            room = Math.Min(room, Math.Max(0, _cat.QtyForVolumeMl(good, budget) - old));
        }
        return room;
    }

    /// <summary>Faucet: adds goods that did not exist. False (nothing changes) when capacity is enforced and the goods do not fit.</summary>
    public bool Create(ushort good, long qty, CreateReason why)
    {
        if (qty < 0) throw new ArgumentOutOfRangeException(nameof(qty));
        if (qty == 0) return true;
        if (qty > Room(good)) return false;
        Set(good, checked(Qty(good) + qty)); _ledger.Created(good, why, qty); _ledger.Trace?.Created(this, good, why, qty);
        return true;
    }

    /// <summary>Faucet that takes what fits; returns the quantity actually created.</summary>
    public long CreateUpTo(ushort good, long qty, CreateReason why)
    {
        long q = Math.Min(qty, Room(good));
        if (q > 0) Create(good, q, why);
        return Math.Max(0, q);
    }

    /// <summary>Sink: removes goods from existence. False (nothing changes) when the store holds less than <paramref name="qty"/>.</summary>
    public bool Destroy(ushort good, long qty, DestroyReason why)
    {
        if (qty < 0) throw new ArgumentOutOfRangeException(nameof(qty));
        if (qty == 0) return true;
        long have = Qty(good);
        if (have < qty) return false;
        Set(good, have - qty); _ledger.Destroyed(good, why, qty); _ledger.Trace?.Destroyed(this, good, why, qty);
        return true;
    }

    /// <summary>Sink that takes what is there; returns the quantity actually destroyed.</summary>
    public long DestroyUpTo(ushort good, long qty, DestroyReason why)
    {
        long q = Math.Min(qty, Qty(good));
        if (q > 0) Destroy(good, q, why);
        return Math.Max(0, q);
    }

    /// <summary>Moves goods between stores; conserves quantity. False (nothing changes) when the source lacks them or the target cannot hold them.</summary>
    public static bool Move(Store from, Store to, ushort good, long qty)
    {
        if (qty < 0) throw new ArgumentOutOfRangeException(nameof(qty));
        if (qty == 0) return true;
        if (ReferenceEquals(from, to)) return from.Qty(good) >= qty;
        if (from.Qty(good) < qty || qty > to.Room(good)) return false;
        from.Set(good, from.Qty(good) - qty); to.Set(good, checked(to.Qty(good) + qty)); from._ledger.Trace?.Moved(from, to, good, qty);
        return true;
    }

    /// <summary>Moves as much as is available and fits; returns the quantity moved.</summary>
    public static long MoveUpTo(Store from, Store to, ushort good, long qty)
    {
        if (ReferenceEquals(from, to)) return 0;
        long q = Math.Min(Math.Min(qty, from.Qty(good)), to.Room(good));
        if (q > 0) Move(from, to, good, q);
        return Math.Max(0, q);
    }

    /// <summary>
    /// One day of spoilage (items spec 3.5): per row <c>loss = qty x ppm x tempMult / 1e9</c> with stochastic rounding keyed
    /// <c>(seed, hourKey, owner, 0x222, good)</c>, so the expectation is exact and the result is a pure function of the key. Returns the quantity destroyed
    /// (reason Spoiled). <paramref name="leakMult"/> is the roof-leak multiplier (1 normally, 10 under a damaged roof).
    /// </summary>
    public long ApplySpoilage(ulong seed, long hourKey, int tempC, int leakMult = 1)
    {
        long lost = 0;
        int mult = GoodCatalog.TempMultPermille(tempC);
        for (int i = _n - 1; i >= 0; i--) // descending: Set may remove the row
        {
            ushort g = _good[i]; long q = _qty[i];
            int ppm = _cat.SpoilPpm(g, Kind);
            if (ppm == 0) continue;
            Int128 num = (Int128)q * ppm * (_cat.TempSensitive(g) ? mult : 1000) * leakMult; // permille units: divide by 1e6 (ppm) x 1000 (permille)
            long whole = (long)(num / 1_000_000_000);
            long frac = (long)(num % 1_000_000_000);                                             // 0 .. 999,999,999 of 1e9
            uint u = DetRng.NextUInt(seed, hourKey, Owner.Value, RngStreams.Spoilage, g);
            if (frac > 0 && (Int128)frac * 4_294_967_296L > (Int128)u * 1_000_000_000) whole++;  // frac/1e9 > u/2^32
            whole = Math.Min(whole, q);
            if (whole > 0) { Destroy(g, whole, DestroyReason.Spoiled); lost += whole; }
        }
        return lost;
    }

    /// <summary>Row copy for saves and hashing, ascending good.</summary>
    public (ushort Good, long Qty)[] Rows()
    {
        var r = new (ushort, long)[_n];
        for (int i = 0; i < _n; i++) r[i] = (_good[i], _qty[i]);
        return r;
    }

    /// <summary>Restores rows from a save (ascending, positive, no ledger effect: the mass ledger is restored with it).</summary>
    internal void LoadRows(ReadOnlySpan<ushort> goods, ReadOnlySpan<long> qty)
    {
        _n = 0;
        for (int i = 0; i < goods.Length; i++)
        {
            if (qty[i] <= 0 || (i > 0 && goods[i] <= goods[i - 1]) || goods[i] >= _cat.Count) throw new InvalidDataException("corrupt store rows");
            Set(goods[i], qty[i]);
        }
    }
}
