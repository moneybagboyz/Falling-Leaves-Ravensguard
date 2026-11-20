using Ravensguard.Sim.Core;

namespace Ravensguard.Sim.Abstract;

/// <summary>
/// What a system sees during one phase of one hour step. A single instance is reused for the hour; <see cref="Phase"/> changes between phases. Systems read the
/// clock fields, draw randomness only through <see cref="Roll"/> / <see cref="Stream"/> (keyed by the hour, so a result never depends on call order), write the
/// world records they own and report through <see cref="Outbox"/>.
/// </summary>
public sealed class TickContext
{
    internal TickContext(SimWorld world, RegionOutbox outbox) { World = world; Outbox = outbox; }

    public SimWorld World { get; }
    public RegionOutbox Outbox { get; }
    public TickPhase Phase { get; internal set; }

    /// <summary>The hour index being stepped (<see cref="GameTime.TotalHours"/> of its start); also the RNG tick key (overview 2.3).</summary>
    public long Hour { get; internal set; }
    public long Day => Hour / 24;
    public int HourOfDay => (int)(Hour % 24);
    public GameTime Time => GameTime.FromGameHours(Hour);
    public int DayOfYear => Time.DayOfYear;
    public Season Season => Time.Season;
    public int Year => Time.Year;
    public ulong Seed => World.Seed;

    /// <summary>First day of a season (day of year 1, 16, 31, 46): the seasonal phase runs on every hour of it.</summary>
    public bool IsSeasonStartDay => (DayOfYear - 1) % TimeConst.DaysPerSeason == 0;

    /// <summary>The slice rule (abstract spec 3.2): a record with dense index <paramref name="index"/> does its daily work in the hour where this is true.</summary>
    public bool IsSlice(int index) => index % 24 == HourOfDay;

    /// <summary>Seasonal slice: settlement (or fief, field range) <paramref name="k"/> runs its seasonal steps at hour <c>k % 24</c> of the season's first day.</summary>
    public bool IsSeasonSlice(int k) => IsSeasonStartDay && k % 24 == HourOfDay;

    /// <summary>One draw of the counter RNG keyed (seed, this hour, entity, stream, draw).</summary>
    public ulong Roll(ulong entity, uint stream, uint draw = 0) => DetRng.Next(Seed, Hour, entity, stream, draw);

    public RngStream Stream(ulong entity, uint stream) => new(Seed, Hour, entity, stream);
}

/// <summary>A chronicle entry drafted by the worker; it is appended to the <see cref="Chronicle"/> at the barrier, in draft order.</summary>
public sealed record PendingChronicle(EntityId Id, GameTime When, string TypeId, IReadOnlyList<EntityId> Subjects, IReadOnlyList<Cause> Causes, IReadOnlyDictionary<string, string> Data,
    string TextKey = "", IReadOnlyList<ChronParam>? Params = null);

/// <summary>
/// What an hour step produces for the main thread (abstract spec 2.5): chronicle entries, events for subscribers and counters. Built by the worker, applied by
/// the barrier in draft order so the chronicle never depends on thread timing. Entry ids are allocated at draft time (the worker is the only allocator) so later
/// entries and causes can reference earlier ones at once.
/// </summary>
public sealed class RegionOutbox
{
    private readonly IdAllocator _ids;
    private readonly List<PendingChronicle> _entries = [];
    private readonly List<object> _events = [];
    private readonly Dictionary<string, long> _counters = new(StringComparer.Ordinal);

    public RegionOutbox(IdAllocator ids) { _ids = ids; }

    public IReadOnlyList<PendingChronicle> Entries => _entries;
    public IReadOnlyList<object> Events => _events;
    public IReadOnlyDictionary<string, long> Counters => _counters;
    public bool IsEmpty => _entries.Count == 0 && _events.Count == 0 && _counters.Count == 0;

    /// <summary>Drafts a chronicle entry and returns its id (usable as a cause by later entries at once).</summary>
    public EntityId Chronicle(GameTime when, string typeId, IReadOnlyList<EntityId> subjects, IReadOnlyList<Cause> causes, params (string Key, string Value)[] data)
    {
        var id = _ids.Allocate(IdKind.Event);
        var d = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (k, v) in data) d[k] = v;
        _entries.Add(new PendingChronicle(id, when, typeId, subjects, causes, d));
        return id;
    }

    /// <summary>Drafts a keyed entry (chronicle-full 4.1, no English in the sim): the text key and typed parameters instead of a sentence; <paramref name="data"/> keeps machine fields only.</summary>
    public EntityId Chronicle(GameTime when, string typeId, string textKey, IReadOnlyList<ChronParam> parameters, IReadOnlyList<EntityId> subjects, IReadOnlyList<Cause> causes, params (string Key, string Value)[] data)
    {
        var id = _ids.Allocate(IdKind.Event);
        var d = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (k, v) in data) d[k] = v;
        _entries.Add(new PendingChronicle(id, when, typeId, subjects, causes, d, textKey, parameters));
        return id;
    }

    public void Publish<T>(T evt) where T : notnull => _events.Add(evt);

    public void Count(string counter, long delta = 1) => _counters[counter] = _counters.GetValueOrDefault(counter) + delta;

    /// <summary>Appends the drafted entries to the chronicle, publishes the events on the bus and returns the counters; the outbox is emptied.</summary>
    internal void ApplyTo(Chronicle chronicle, SimEvents? bus, IDictionary<string, long> totals)
    {
        foreach (var e in _entries) chronicle.Add(new ChronicleEntry(e.Id, e.When, e.TypeId, e.Subjects, e.Causes, e.Data, e.TextKey, e.Params));
        if (bus != null) foreach (var ev in _events) PublishBoxed(bus, ev);
        foreach (var (k, v) in _counters) totals[k] = (totals.TryGetValue(k, out var cur) ? cur : 0) + v;
        _entries.Clear(); _events.Clear(); _counters.Clear();
    }

    private static readonly System.Reflection.MethodInfo PublishMethod = typeof(SimEvents).GetMethod(nameof(SimEvents.Publish))!;
    private static readonly Dictionary<Type, System.Reflection.MethodInfo> Closed = [];
    private static void PublishBoxed(SimEvents bus, object evt)
    {
        var t = evt.GetType();
        lock (Closed) { if (!Closed.TryGetValue(t, out var m)) Closed[t] = m = PublishMethod.MakeGenericMethod(t); m.Invoke(bus, [evt]); }
    }
}
