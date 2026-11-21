using Ravensguard.Sim.Core;

namespace Ravensguard.Sim.Economy;

/// <summary>
/// The only way coin changes hands (money spec 2.2). Purses are owned by a person, household, business, settlement, treasury, chest, hoard, market or caravan.
/// <see cref="Transfer"/> moves coin between two purses and logs it with a reason; <see cref="Mint"/> and <see cref="Sink"/> are the documented sources and
/// sinks, each logged as such. Balances live in a private array that nothing outside this class can write (the NoDirectBalanceWrites rule), a balance never
/// goes below zero, and arithmetic is checked. Not thread-safe: one writer at a time (overview E3).
/// </summary>
public sealed class Ledger
{
    public static readonly int TransferReasons = Enum.GetValues<TransferReason>().Length;
    public static readonly int SourceReasons = Enum.GetValues<SourceReason>().Length;
    public static readonly int SinkReasons = Enum.GetValues<SinkReason>().Length;

    private long[] _balance = new long[64];
    private EntityId[] _owner = new EntityId[64];
    private PurseKind[] _kind = new PurseKind[64];
    private int _count;
    private readonly Dictionary<(PurseKind, EntityId), int> _index = [];

    private readonly long[] _sourced = new long[SourceReasons];
    private readonly long[] _sunk = new long[SinkReasons];
    private readonly long[] _flowCoin = new long[TransferReasons];
    private readonly long[] _flowCount = new long[TransferReasons];
    private long _seq;
    private long[]? _in, _out;          // per purse x transfer reason, only while tracking (diagnostics: who earns what)

    /// <summary>P7-CN1 (coinage 5.2): the gold book. Null in a world that never struck gold; set by the coinage wiring or the first strike. One null test on every transfer when absent.</summary>
    public CoinStock? Coinage { get; set; }

    private readonly LedgerEntry[] _recent;
    private int _recentHead, _recentCount;

    /// <param name="recentLogCapacity">How many of the latest movements are kept for inspection (a ring); totals by reason are kept forever.</param>
    public Ledger(int recentLogCapacity = 4096) { _recent = new LedgerEntry[Math.Max(0, recentLogCapacity)]; }

    public int PurseCount => _count;
    /// <summary>Movements logged so far (sources, sinks and transfers).</summary>
    public long Sequence => _seq;

    // ---------- purses ----------

    /// <summary>The purse of this owner and kind, created empty on first use (idempotent). Purses are never removed, so indices stay stable.</summary>
    public PurseId Open(PurseKind kind, EntityId owner)
    {
        if (_index.TryGetValue((kind, owner), out int i)) return new PurseId(i);
        if (_count == _balance.Length)
        {
            Array.Resize(ref _balance, _count * 2); Array.Resize(ref _owner, _count * 2); Array.Resize(ref _kind, _count * 2);
        }
        _owner[_count] = owner; _kind[_count] = kind; _index[(kind, owner)] = _count;
        return new PurseId(_count++);
    }

    public bool TryFind(PurseKind kind, EntityId owner, out PurseId id)
    {
        if (_index.TryGetValue((kind, owner), out int i)) { id = new PurseId(i); return true; }
        id = PurseId.None; return false;
    }

    private void Check(PurseId p) { if ((uint)p.Index >= (uint)_count) throw new ArgumentOutOfRangeException(nameof(p), $"unknown purse {p}"); }

    public Coin Balance(PurseId p) { Check(p); return new Coin(_balance[p.Index]); }
    public PurseKind KindOf(PurseId p) { Check(p); return _kind[p.Index]; }
    public EntityId OwnerOf(PurseId p) { Check(p); return _owner[p.Index]; }

    // ---------- the mutators ----------

    /// <summary>
    /// Moves <paramref name="amount"/> from one purse to another. False (nothing changes) when the source holds less; never leaves a negative balance.
    /// Throws for an amount of zero or less, or the same purse on both sides.
    /// </summary>
    public bool Transfer(PurseId from, PurseId to, Coin amount, TransferReason why, EntityId? chronicleEvent = null)
    {
        Check(from); Check(to);
        if (amount.Mites <= 0) throw new ArgumentOutOfRangeException(nameof(amount), "a transfer must be positive");
        if (from == to) throw new ArgumentException("a purse cannot pay itself", nameof(to));
        if (_balance[from.Index] < amount.Mites) return false;
        long newTo = checked(_balance[to.Index] + amount.Mites);
        _balance[from.Index] -= amount.Mites; _balance[to.Index] = newTo;
        _flowCoin[(int)why] = checked(_flowCoin[(int)why] + amount.Mites); _flowCount[(int)why]++;
        if (_in != null) Track(from.Index, to.Index, (int)why, amount.Mites);
        Coinage?.AfterTransfer(this, from, to, amount.Mites);
        Observer?.Invoke(from, to, why, amount.Mites);
        Tap?.Invoke(from, to, why, amount.Mites);
        Log(from, to, amount, (byte)why, false, false, chronicleEvent);
        return true;
    }

    /// <summary>
    /// Creates coin. A documented faucet: <see cref="SourceReason.Seed"/> once at world creation (before day 0) and <see cref="SourceReason.Mint"/> when a mint
    /// strikes gold, silver or copper from metal it took in (see <see cref="MintMath"/> and <see cref="MintWorld"/>). Nothing else may call it.
    /// </summary>
    public void Mint(PurseId into, Coin amount, SourceReason why, EntityId? chronicleEvent = null)
    {
        Check(into);
        if (amount.Mites <= 0) throw new ArgumentOutOfRangeException(nameof(amount), "a mint must be positive");
        long newBal = checked(_balance[into.Index] + amount.Mites);
        long newSrc = checked(_sourced[(int)why] + amount.Mites);
        _balance[into.Index] = newBal; _sourced[(int)why] = newSrc;
        Log(PurseId.None, into, amount, (byte)why, true, false, chronicleEvent);
    }

    /// <summary>Destroys coin. A documented drain: <see cref="SinkReason.Melt"/> (coin into silver) and test-only <see cref="SinkReason.Burn"/>. False when the purse holds less.</summary>
    public bool Sink(PurseId from, Coin amount, SinkReason why, EntityId? chronicleEvent = null)
    {
        Check(from);
        if (amount.Mites <= 0) throw new ArgumentOutOfRangeException(nameof(amount), "a sink must be positive");
        if (_balance[from.Index] < amount.Mites) return false;
        _balance[from.Index] -= amount.Mites; _sunk[(int)why] = checked(_sunk[(int)why] + amount.Mites);
        Coinage?.AfterSink(this, from);
        Log(from, PurseId.None, amount, (byte)why, false, true, chronicleEvent);
        return true;
    }

    /// <summary>Called after every successful transfer (from, to, reason, mites): the soak's coin-flow trace. A diagnostic, not part of the state; it must not move coin.</summary>
    public Action<PurseId, PurseId, TransferReason, long>? Observer { get; set; }

    /// <summary>The treasuries' ledger view (taxes spec, <c>Treasuries</c>): called after every successful transfer like <see cref="Observer"/>, but owned by the economy, so a diagnostic can take the Observer. Must not move coin.</summary>
    public Action<PurseId, PurseId, TransferReason, long>? Tap { get; set; }

    /// <summary>Starts (or restarts, zeroed) per-purse tracking of the coin moved in and out by reason: a diagnostic, not part of the state.</summary>
    public void StartTracking() { _in = new long[Math.Max(64, _count * 2) * TransferReasons]; _out = new long[_in.Length]; }
    public Coin InFlow(PurseId p, TransferReason why) => new(_in == null || (p.Index + 1) * TransferReasons > _in.Length ? 0 : _in[p.Index * TransferReasons + (int)why]);
    public Coin OutFlow(PurseId p, TransferReason why) => new(_out == null || (p.Index + 1) * TransferReasons > _out.Length ? 0 : _out[p.Index * TransferReasons + (int)why]);
    private void Track(int from, int to, int why, long mites)
    {
        int need = (Math.Max(from, to) + 1) * TransferReasons;
        if (need > _in!.Length) { int n = Math.Max(need, _in.Length * 2); Array.Resize(ref _in, n); Array.Resize(ref _out, n); }
        _out![from * TransferReasons + why] += mites; _in[to * TransferReasons + why] += mites;
    }

    private void Log(PurseId from, PurseId to, Coin amount, byte reason, bool src, bool sink, EntityId? ev)
    {
        long seq = _seq++;
        if (_recent.Length == 0) return;
        _recent[(_recentHead + _recentCount) % _recent.Length] = new LedgerEntry(seq, from, to, amount, reason, src, sink, ev);
        if (_recentCount < _recent.Length) _recentCount++; else _recentHead = (_recentHead + 1) % _recent.Length;
    }

    // ---------- inspection ----------

    /// <summary>The latest movements, oldest first (at most the log capacity).</summary>
    public IEnumerable<LedgerEntry> Recent() { for (int k = 0; k < _recentCount; k++) yield return _recent[(_recentHead + k) % _recent.Length]; }

    /// <summary>Total coin moved by transfers of this reason, and how many transfers.</summary>
    public Coin Flow(TransferReason why) => new(_flowCoin[(int)why]);
    public long FlowCount(TransferReason why) => _flowCount[(int)why];
    public Coin Sourced(SourceReason why) => new(_sourced[(int)why]);
    public Coin Sunk(SinkReason why) => new(_sunk[(int)why]);

    /// <summary>Recomputes the conservation audit from the balances.</summary>
    public ConservationReport Audit()
    {
        long total = 0, hoards = 0;
        for (int i = 0; i < _count; i++) { total = checked(total + _balance[i]); if (_kind[i] == PurseKind.Hoard) hoards += _balance[i]; }
        return new ConservationReport(new Coin(total), new Coin(hoards), Sourced(SourceReason.Seed), Sourced(SourceReason.Mint),
            Sunk(SinkReason.Melt), Sunk(SinkReason.Burn), _count, _flowCount.Sum(), Sourced(SourceReason.Immigrate), Sunk(SinkReason.Emigrate));
    }

    /// <summary>Sum of the balances of every purse of this kind.</summary>
    public Coin TotalOf(PurseKind kind)
    {
        long s = 0;
        for (int i = 0; i < _count; i++) if (_kind[i] == kind) s = checked(s + _balance[i]);
        return new Coin(s);
    }

    // ---------- save support ----------

    public sealed record State(ulong[] Owner, byte[] Kind, long[] Balance, long[] Sourced, long[] Sunk, long[] FlowCoin, long[] FlowCount, long Seq);

    public State Export()
    {
        var owner = new ulong[_count]; var kind = new byte[_count]; var bal = new long[_count];
        for (int i = 0; i < _count; i++) { owner[i] = _owner[i].Value; kind[i] = (byte)_kind[i]; bal[i] = _balance[i]; }
        // the flow counters are trimmed of trailing zeros down to the pre-Phase 7 count, so a world that never used the Phase 7 reasons keeps its bytes (Import pads shorter arrays)
        int keep = (int)TransferReason.RelicSale + 1;
        for (int r = TransferReasons - 1; r >= keep; r--) if (_flowCoin[r] != 0 || _flowCount[r] != 0) { keep = r + 1; break; }
        return new State(owner, kind, bal, (long[])_sourced.Clone(), (long[])_sunk.Clone(), _flowCoin[..keep], _flowCount[..keep], _seq);
    }

    /// <summary>Rebuilds a ledger from saved state and checks that it is conserved; throws <see cref="InvalidDataException"/> otherwise.</summary>
    public static Ledger Import(State s, int recentLogCapacity = 4096)
    {
        if (s.Owner.Length != s.Kind.Length || s.Owner.Length != s.Balance.Length || s.Sourced.Length > SourceReasons || s.Sunk.Length > SinkReasons
            || s.FlowCoin.Length > TransferReasons || s.FlowCount.Length != s.FlowCoin.Length) throw new InvalidDataException("corrupt ledger state");
        var l = new Ledger(recentLogCapacity);
        for (int i = 0; i < s.Owner.Length; i++)
        {
            if (s.Balance[i] < 0 || s.Kind[i] >= Enum.GetValues<PurseKind>().Length) throw new InvalidDataException("corrupt purse");
            var p = l.Open((PurseKind)s.Kind[i], new EntityId(s.Owner[i]));
            if (p.Index != i) throw new InvalidDataException("duplicate purse in ledger state");
            l._balance[i] = s.Balance[i];
        }
        Array.Copy(s.Sourced, l._sourced, s.Sourced.Length); Array.Copy(s.Sunk, l._sunk, s.Sunk.Length);   // a save older than the migration reasons has shorter arrays
        Array.Copy(s.FlowCoin, l._flowCoin, s.FlowCoin.Length); Array.Copy(s.FlowCount, l._flowCount, s.FlowCount.Length);   // a save older than the Alms reason has shorter arrays
        l._seq = s.Seq;
        if (!l.Audit().IsConserved) throw new InvalidDataException("saved ledger does not balance");
        return l;
    }
}
