namespace Ravensguard.Sim.Goods;

/// <summary>Why goods came into existence (items spec 2.2). Each is a documented faucet counted by the <see cref="MassLedger"/>.</summary>
public enum CreateReason : byte { Harvest, Mined, Cut, Caught, Hunted, Brewed, Crafted, Seed, Import, Dematerialised, Other }

/// <summary>Why goods ceased to exist (items spec 2.2). Each is a documented sink counted by the <see cref="MassLedger"/>.</summary>
public enum DestroyReason : byte { Eaten, Burned, Spoiled, Worn, Sown, Consumed, Lost, Export, Materialised, Other,
    /// <summary>Phase 7 (overview 8.1, append only): tailings and slag, and forage trampled by a host.</summary>
    Waste, Trampled }

/// <summary>
/// Per good and reason counters of every create and destroy, for the mass audit: <c>created - destroyed == stock delta</c> exactly (items spec 2.2, soak 9.1).
/// Shared by all stores of one economy; nothing else may change a stock quantity.
/// </summary>
/// <summary>Optional observer of every create, destroy and move of goods (the soak's goods-flow trace); null in normal runs.</summary>
public interface IFlowTrace
{
    void Created(Store store, ushort good, CreateReason why, long qty);
    void Destroyed(Store store, ushort good, DestroyReason why, long qty);
    void Moved(Store from, Store to, ushort good, long qty);
}

public sealed class MassLedger
{
    /// <summary>The flow observer, if any (diagnostics only; never part of the state).</summary>
    public IFlowTrace? Trace { get; set; }
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
    /// <summary>The destroy reasons every save before Phase 7 holds per good (<see cref="DestroyReason.Other"/> was the last).</summary>
    public const int LegacyDestroyReasons = (int)DestroyReason.Other + 1;

    /// <summary>
    /// The per-good destroyed counters, <b>compact</b>: each row is as wide as the highest reason any good has used (never narrower than <see cref="LegacyDestroyReasons"/>), so a world that never wastes or tramples
    /// saves the bytes it saved before the Phase 7 reasons were appended, and a save from before them restores into the wider table (<see cref="Restore"/>).
    /// </summary>
    public long[] DestroyedState()
    {
        int w = LegacyDestroyReasons;
        for (int g = 0; g < GoodCount; g++) for (int r = w; r < DestroyReasons; r++) if (_destroyed[g * DestroyReasons + r] != 0) w = r + 1;
        if (w == DestroyReasons) return (long[])_destroyed.Clone();
        var o = new long[GoodCount * w];
        for (int g = 0; g < GoodCount; g++) Array.Copy(_destroyed, g * DestroyReasons, o, g * w, w);
        return o;
    }

    /// <summary>The row width of a saved destroyed array (<see cref="DestroyedState"/>), or -1 when its length fits no width of this build.</summary>
    public static int DestroyedWidth(int goodCount, int length)
    {
        if (goodCount == 0) return length == 0 ? DestroyReasons : -1;
        if (length % goodCount != 0) return -1;
        int w = length / goodCount;
        return w >= LegacyDestroyReasons && w <= DestroyReasons ? w : -1;
    }

    public void Restore(long[] created, long[] destroyed)
    {
        int w = DestroyedWidth(GoodCount, destroyed.Length);
        if (created.Length != _created.Length || w < 0) throw new ArgumentException("mass ledger shape mismatch");
        Array.Copy(created, _created, created.Length);
        for (int g = 0; g < GoodCount; g++) Array.Copy(destroyed, g * w, _destroyed, g * DestroyReasons, w);       // a save narrower than this build's reasons leaves the new ones at 0
    }
}
