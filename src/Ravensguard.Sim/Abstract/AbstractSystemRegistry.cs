using System.Reflection;
using Ravensguard.Sim.People.Vital;

namespace Ravensguard.Sim.Abstract;

/// <summary>
/// The systems the abstract tick runs, with explicit ordering: inside a phase systems run by ascending <see cref="IAbstractSystem.Order"/>, ties by name
/// (ordinal), never by registration or reflection order. Registration after the ticker started is refused.
/// </summary>
public sealed class AbstractSystemRegistry
{
    private readonly List<IAbstractSystem> _systems = [];
    private readonly Dictionary<TickPhase, IAbstractSystem[]> _byPhase = [];
    private bool _frozen;

    public IReadOnlyList<IAbstractSystem> All => Ordered(_systems);
    public int Count => _systems.Count;

    private static IReadOnlyList<IAbstractSystem> Ordered(IEnumerable<IAbstractSystem> s)
        => s.OrderBy(x => x.Order).ThenBy(x => x.Name, StringComparer.Ordinal).ToList();

    /// <summary>Registers a system; the name must be unique and at least one phase set.</summary>
    public AbstractSystemRegistry Add(IAbstractSystem system)
    {
        if (_frozen) throw new InvalidOperationException("The registry is frozen: a ticker already uses it.");
        if (system.Phases == TickPhase.None) throw new ArgumentException($"System '{system.Name}' lists no phase.", nameof(system));
        if (_systems.Any(x => x.Name == system.Name)) throw new ArgumentException($"A system named '{system.Name}' is already registered.", nameof(system));
        _systems.Add(system);
        _byPhase.Clear();
        return this;
    }

    public bool Remove(string name)
    {
        if (_frozen) throw new InvalidOperationException("The registry is frozen: a ticker already uses it.");
        int n = _systems.RemoveAll(x => x.Name == name);
        _byPhase.Clear();
        return n > 0;
    }

    public IAbstractSystem? Find(string name) => _systems.FirstOrDefault(x => x.Name == name);

    /// <summary>The systems that take part in a phase, in running order.</summary>
    public IReadOnlyList<IAbstractSystem> For(TickPhase phase)
    {
        if (!_byPhase.TryGetValue(phase, out var a)) _byPhase[phase] = a = Ordered(_systems.Where(s => (s.Phases & phase) != 0)).ToArray();
        return a;
    }

    internal void Freeze() => _frozen = true;

    /// <summary>
    /// Registers every class in <paramref name="assembly"/> carrying <see cref="AbstractSystemAttribute"/> (public, parameterless constructor), types in ordinal
    /// full-name order. Returns how many were added; a system already registered by name is skipped.
    /// </summary>
    public int Discover(Assembly assembly)
    {
        int n = 0;
        foreach (var t in assembly.GetTypes().Where(t => t.IsClass && !t.IsAbstract && typeof(IAbstractSystem).IsAssignableFrom(t) && t.GetCustomAttribute<AbstractSystemAttribute>() != null)
                     .OrderBy(t => t.FullName, StringComparer.Ordinal))
        {
            if (t.GetConstructor(Type.EmptyTypes) == null) throw new InvalidOperationException($"{t.FullName} is marked [AbstractSystem] but has no public parameterless constructor.");
            var s = (IAbstractSystem)Activator.CreateInstance(t)!;
            if (_systems.Any(x => x.Name == s.Name)) continue;
            Add(s); n++;
        }
        return n;
    }

    /// <summary>
    /// The registry of the game. REGISTRATION POINT: a system that implements <see cref="IAbstractSystem"/> either carries <see cref="AbstractSystemAttribute"/>
    /// (found by <see cref="Discover"/>) or gets one explicit line below. Its position comes from its own <see cref="IAbstractSystem.Order"/>
    /// (a <see cref="SystemOrder"/> constant), so where the line sits does not matter.
    /// </summary>
    public static AbstractSystemRegistry CreateDefault(bool discover = true)
    {
        var r = new AbstractSystemRegistry();
        // --- Phase 2 systems, one line each ---
        r.Add(new HouseholdNeedsSystem());          // population + households spec: eat, drink, heat, nutrition, spoilage (H4)
        r.Add(new VitalRollsSystem());              // population spec 3.1-3.2: births, conceptions, deaths (H4)
        r.Add(new PopulationDailySystem());         // population spec 3.3, 3.5, 3.6: outbreaks, famine, weekly marriage catch-up (H5)
        r.Add(new PopulationSeasonalSystem());      // population spec 3.3-3.4: marriage matching, dissolution sweep (H6, S6)
        r.Add(new PopulationMigrationSystem());     // population spec 3.7: crowding and marriage-wait emigration (H6, S7)
        // Farming (S1, S2, H2), Markets (H3, H5), Production (H2, H4), Taxes (S4) register here or by [AbstractSystem].
        if (discover) r.Discover(typeof(AbstractSystemRegistry).Assembly);
        return r;
    }
}
