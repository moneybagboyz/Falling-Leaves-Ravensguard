using System.Security.Cryptography;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.People;
using Ravensguard.Sim.People.Vital;
using Ravensguard.Sim.Save;

namespace Ravensguard.Sim.Abstract;

/// <summary>The saved progress of the abstract tick (the "abstract" save section): which hour was last completed and the vital counters.</summary>
public sealed class AbstractState
{
    /// <summary>Hour index (<see cref="GameTime.TotalHours"/>) of the last completed hour step; the next step is <c>HourDone + 1</c>.</summary>
    public long HourDone { get; set; }
    public VitalState Vital { get; set; } = new();
}

/// <summary>
/// Everything the abstract systems read and write: the population, the economy, the chronicle and id allocator, the vital state and a small service bag for
/// state other systems own (a farming table, a market book set). Records are mutated only by the worker that runs the hour step and by the command pump at a
/// barrier (single writer, abstract spec A1).
/// </summary>
public sealed class SimWorld
{
    private readonly Dictionary<Type, object> _services = [];
    private ChildIndex? _children;

    public ulong Seed { get; }
    public Population Population { get; }
    public EconomyState Economy { get; }
    public Chronicle Chronicle { get; }
    public IdAllocator Ids { get; }
    public AbstractState State { get; }
    public VitalState Vital => State.Vital;
    public NamePools? Names { get; }
    public VitalRates Rates { get; set; } = VitalRates.Default;
    public IHourEnvironment Env { get; set; }

    public PersonTable People => Population.People;
    public HouseholdTable Households => Population.Households;

    public SimWorld(ulong seed, Population population, EconomyState economy, Chronicle? chronicle = null, IdAllocator? ids = null, AbstractState? state = null,
        NamePools? names = null, IHourEnvironment? env = null)
    {
        Seed = seed; Population = population; Economy = economy; Chronicle = chronicle ?? new Chronicle(); Ids = ids ?? new IdAllocator(); Names = names;
        State = state ?? new AbstractState();
        Env = env ?? new SeasonalEnvironment(seed);
        if (state == null || state.Vital.FamineStreak.Length == 0) State.Vital = VitalState.For(population);
        State.Vital.EnsureSettlements(population.Region.Settlements.Count);
    }

    /// <summary>A world from a saved or newly created <see cref="WorldState"/>; the state's <c>Abstract</c> section is created (and attached) when missing.</summary>
    public static SimWorld From(WorldState w, NamePools? names = null, IHourEnvironment? env = null)
    {
        if (w.Economy == null) throw new InvalidOperationException("The abstract tick needs the economy section of the world.");
        bool fresh = w.Abstract == null;
        var st = w.Abstract ?? new AbstractState { HourDone = w.Clock.Now.TotalHours };
        var sw = new SimWorld(w.Seed, w.Population, w.Economy, w.Chronicle, w.Ids, st, names, env);
        if (fresh)
        {
            w.Abstract = st;
            VitalSeeding.SeedPregnancies(sw, w.Clock.Now);
        }
        return sw;
    }

    /// <summary>Children by person, built on first use and kept current by births.</summary>
    public ChildIndex Children => _children ??= new ChildIndex(Population.People);

    /// <summary>Called by births so the child index stays current once it exists.</summary>
    public void NoteBirth(int child) => _children?.Add(Population.People, child);

    public T? Get<T>() where T : class => _services.TryGetValue(typeof(T), out var o) ? (T)o : null;
    public T Require<T>() where T : class => Get<T>() ?? throw new InvalidOperationException($"No {typeof(T).Name} registered in the world services.");
    public void Set<T>(T service) where T : class => _services[typeof(T)] = service;

    /// <summary>The age in whole years of a person on a game day.</summary>
    public int AgeOn(int person, long day) => Math.Max(0, (int)(day - People.BirthDay[person]) / TimeConst.DaysPerYear);

    /// <summary>
    /// One digest of the running world (population, vital columns, economy, chronicle size, counters) for the determinism tests: equal worlds give equal hashes,
    /// however the ticks were batched.
    /// </summary>
    public string ComputeHash()
    {
        using var h = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var b8 = new byte[8];
        void L(long v) { System.Buffers.Binary.BinaryPrimitives.WriteInt64LittleEndian(b8, v); h.AppendData(b8); }
        void S(string s) => h.AppendData(System.Text.Encoding.ASCII.GetBytes(s));
        S(Population.ComputeHash()); S(Population.ComputeSimHash()); S(Economy.ComputeHash());
        L(Chronicle.Count); L(State.HourDone); L(Vital.Births); L(Vital.Marriages); L(Vital.Immigrants); L(Vital.Emigrants); L(Vital.Alive);
        foreach (long d in Vital.DeathsByCause) L(d);
        return Convert.ToHexString(h.GetHashAndReset());
    }
}
