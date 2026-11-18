namespace Ravensguard.Sim.Goods;

/// <summary>Why goods came into existence (items spec 2.2). Each is a documented faucet counted by the <see cref="MassLedger"/>.</summary>
public enum CreateReason : byte { Harvest, Mined, Cut, Caught, Hunted, Brewed, Crafted, Seed, Import, Dematerialised, Other }

/// <summary>Why goods ceased to exist (items spec 2.2). Each is a documented sink counted by the <see cref="MassLedger"/>.</summary>
public enum DestroyReason : byte { Eaten, Burned, Spoiled, Worn, Sown, Consumed, Lost, Export, Materialised, Other }

/// <summary>
/// Per good and reason counters of every create and destroy, for the mass audit: <c>created - destroyed == stock delta</c> exactly (items spec 2.2, soak 9.1).
/// Shared by all stores of one economy; nothing else may change a stock quantity.
/// </summary>
public sealed class MassLedger
{
    public static readonly int CreateReasons = Enum.GetValues<CreateReason>().Length;
    public static readonly int DestroyReasons = Enum.GetValues<DestroyReason>().Length;
    private readonly long[] _created, _destroyed;
    public int GoodCount { get; }

    public MassLedger(int goodCount)
    {
        GoodCount = goodCount;
        _created = new long[goodCount * CreateReasons]; _destroyed = new long[goodCount * DestroyReasons];
    }

    internal void Created(ushort good, CreateReason r, long qty) => _created[good * CreateReasons + (int)r] = checked(_created[good * CreateReasons + (int)r] + qty);
    internal void Destroyed(ushort good, DestroyReason r, long qty) => _destroyed[good * DestroyReasons + (int)r] = checked(_destroyed[good * DestroyReasons + (int)r] + qty);

    public long Created(ushort good, CreateReason r) => _created[good * CreateReasons + (int)r];
    public long Destroyed(ushort good, DestroyReason r) => _destroyed[good * DestroyReasons + (int)r];

    public long TotalCreated(ushort good) { long s = 0; for (int r = 0; r < CreateReasons; r++) s += _created[good * CreateReasons + r]; return s; }
    public long TotalDestroyed(ushort good) { long s = 0; for (int r = 0; r < DestroyReasons; r++) s += _destroyed[good * DestroyReasons + r]; return s; }
    /// <summary>Created minus destroyed: must equal the sum of that good over every store.</summary>
    public long Net(ushort good) => TotalCreated(good) - TotalDestroyed(good);

    public long[] CreatedState() => (long[])_created.Clone();
    public long[] DestroyedState() => (long[])_destroyed.Clone();

    public void Restore(long[] created, long[] destroyed)
    {
        if (created.Length != _created.Length || destroyed.Length != _destroyed.Length) throw new ArgumentException("mass ledger shape mismatch");
        Array.Copy(created, _created, created.Length); Array.Copy(destroyed, _destroyed, destroyed.Length);
    }
}
