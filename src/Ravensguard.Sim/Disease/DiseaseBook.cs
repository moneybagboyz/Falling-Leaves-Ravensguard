using Ravensguard.Sim.Core;

namespace Ravensguard.Sim.Disease;

/// <summary>One person's everyday illness (disease-and-plague 3): tracked persons only (the hero, his party and kin, whoever the wiring names).</summary>
public sealed class Illness
{
    public ulong Person;
    public IllnessKind Kind;
    public int StartDay;
    /// <summary>Today's care (set by the daily pass from the days below and the household).</summary>
    public IllnessCare Care;
    /// <summary>Cared for at some point: the course is a day shorter (4.1).</summary>
    public bool Cared;
    /// <summary>The one death check has been made.</summary>
    public bool Checked;
    /// <summary>Last day a party member nursed him or a physician is asked to call, from the hero's commands (-1 none).</summary>
    public int NursedUntil = -1, PhysicianUntil = -1;
    /// <summary>Where he fell ill (the physician is that settlement's).</summary>
    public int Settlement = -1;
    /// <summary>The chronicle entry of the onset (the death's cause chain starts here) and its cause key ("the flux after drinking from the Wend at camp").</summary>
    public ulong Entry;
    public string OnsetKey = "";
}

/// <summary>One person ill with the great plague (4.3): when he caught it, the day he lives or dies, the day it is over.</summary>
public sealed class PlagueCase
{
    public ulong Person;
    public int Settlement, InfectedDay, DeathDay, EndDay;
    public int NursedUntil = -1, PhysicianUntil = -1;
    public bool Checked;
}

/// <summary>A settlement's epidemic (4.3): when it began and ends, the daily chance each susceptible person catches it, and its tolls.</summary>
public sealed class Epidemic
{
    public int Settlement, StartDay, EndDay, DailyPpm, PopStart, Dead, Infected;
    public ulong Entry;
    public bool Active = true;
}

/// <summary>The great plague as one region lives it (disease-and-plague 3, saved in the "disease" section): its name, where it began, its epidemics, cases, the immune and the dead.</summary>
public sealed class GreatPlague
{
    public int Id, NameIndex, OriginRegion;
    public int StartDay;
    public ulong BeginEntry;
    public bool Active = true;
    public int Dead, Recovered;
    public SortedDictionary<int, Epidemic> Epidemics { get; } = [];
    public SortedDictionary<ulong, PlagueCase> Cases { get; } = [];
    public SortedSet<ulong> Immune { get; } = [];
    /// <summary>Settlements that closed their gates: until this day (4.4).</summary>
    public SortedDictionary<int, int> Quarantine { get; } = [];
    /// <summary>Households that burnt the dead's bedding: until this day (contagion in the household halved).</summary>
    public SortedDictionary<int, int> Burned { get; } = [];

    public int ActiveEpidemics => Epidemics.Values.Count(e => e.Active);
    public bool Quarantined(int settlement, int day) => Quarantine.TryGetValue(settlement, out int until) && until >= day;
}

/// <summary>The scarcity terms (4.5): wages and the price of land, permille of what was calibrated, with the peaks they are measured from.</summary>
public sealed class ScarcityTerms
{
    public int WageMultPm = 1000, LandMultPm = 1000;
    public int PeakWorkers, PeakHouseholds;
    public int LastDay = -1;
    public bool IsDefault => WageMultPm == 1000 && LandMultPm == 1000 && PeakWorkers == 0 && PeakHouseholds == 0 && LastDay < 0;
}

/// <summary>
/// The great plague as the continent holds it (4.2, 4.3): one at a time, with the regions it is in and the seasons they are in it. Mutated only by the continent tick at its seasonal barrier;
/// a detailed region reports itself in <see cref="Reports"/> (one writer, the region's own worker).
/// </summary>
public sealed class ContinentPlague
{
    public bool Active;
    /// <summary>The plague's serial in this world (1 for the first) and its name's index in <see cref="PlagueNames"/>.</summary>
    public int Id, NameIndex, Origin = -1, StartDay;
    public long StartSeason;
    public PlagueSeverity Severity { get; set; } = PlagueSeverity.Normal;
    /// <summary>Region to (first season, last season it is in) for the regions the tick treats in closed form.</summary>
    public SortedDictionary<int, (long From, long Until)> Infected { get; } = [];
    /// <summary>A detailed region that has the plague in it: the last day it said so.</summary>
    public SortedDictionary<int, int> Reports { get; } = [];
    public int DeadEstimate;
    /// <summary>The day of the tick (set by it): a report counts for 15 days.</summary>
    public int NowDay;
    private readonly object _gate = new();
    /// <summary>News the tick has not yet sent (kind plague, origin, magnitude).</summary>
    public List<(int Region, int Magnitude)> PendingNews { get; } = [];

    public static readonly string[] PlagueNames = ["plague_name_grey_death", "plague_name_sweating_sickness", "plague_name_red_plague", "plague_name_wasting", "plague_name_black_cough", "plague_name_pale_fever"];
    public string NameKey => PlagueNames[Math.Clamp(NameIndex, 0, PlagueNames.Length - 1)];

    public void Report(int region, int day) { lock (_gate) Reports[region] = day; }
    public bool IsInfected(int region) => Infected.ContainsKey(region) || (Reports.TryGetValue(region, out int d) && NowDay - d <= 15);
    public bool IsBlank => !Active && Id == 0 && Infected.Count == 0 && Reports.Count == 0;
}

/// <summary>
/// The disease state of the home region (disease-and-plague 3): the everyday illnesses of the tracked persons, the great plague as the region lives it, the scarcity terms and a reference to the
/// continent's plague (saved with them). Absent from a save until anything happens. The hooks (<see cref="Tracked"/>, the exposure and water readers) are wired by <see cref="DiseaseWiring"/>.
/// </summary>
public sealed class DiseaseBook
{
    /// <summary>Everyday illnesses by person id (one at a time).</summary>
    public SortedDictionary<ulong, Illness> Illnesses { get; } = [];
    public GreatPlague? Plague { get; set; }
    public ScarcityTerms Scarcity { get; set; } = new();
    public ContinentPlague Continent { get; set; } = new();
    /// <summary>Count of plagues this region has had (the next one's immunity starts clean).</summary>
    public int LastDay = -1;

    public PlagueSeverity Severity { get => Continent.Severity; set => Continent.Severity = value; }

    /// <summary>Who is tracked today: the hero first, then his household and party, in ascending id (null: nobody).</summary>
    public Func<IReadOnlyList<TrackedPerson>>? Tracked { get; set; }
    /// <summary>Hours of the day a person was cold and wet (exposure band Cold or worse); null: the household's cold grievance counts 6.</summary>
    public Func<ulong, int>? ExposureHours { get; set; }
    /// <summary>Days a person has drunk unboiled camp water and the people of the army camp he is in; null: none.</summary>
    public Func<ulong, int>? UnboiledWaterDays { get; set; }
    public Func<ulong, int>? ArmyCampSize { get; set; }
    /// <summary>Whether a person slept on marsh ground today; null: no.</summary>
    public Func<ulong, bool>? OnMarsh { get; set; }
    /// <summary>The hero died of an illness (his death is the family's pending death): person, the time, the cause, the outbox; null: the hero cannot die of one.</summary>
    public Action<ulong, GameTime, Cause, Ravensguard.Sim.Abstract.RegionOutbox>? HeroDies { get; set; }
    /// <summary>The coin hook for a physician's fee is the ledger; this tells which purse the hero pays from (the person purse), or null for his household's.</summary>
    public Func<ulong, Ravensguard.Sim.Economy.PurseId?>? PurseOf { get; set; }

    public bool IsBlank => Illnesses.Count == 0 && Plague == null && Scarcity.IsDefault && Continent.IsBlank;

    public Illness? IllnessOf(ulong person) => Illnesses.TryGetValue(person, out var i) ? i : null;
    public bool Immune(ulong person) => Plague != null && Plague.Immune.Contains(person);

    /// <summary>The <c>DiseaseHash</c> of the daily trail (disease-and-plague 5): illnesses, the plague and its cases, the scarcity terms and the continent's plague, in ascending order.</summary>
    public ulong Hash()
    {
        ulong h = 0xCBF29CE484222325UL;
        void M(ulong v) => h = DetRng.Mix(h ^ (v + 0x9E3779B97F4A7C15UL));
        void I(long v) => M((ulong)v);
        foreach (var (k, i) in Illnesses) { M(k); M((ulong)i.Kind); I(i.StartDay); M((ulong)i.Care); M(i.Cared ? 1UL : 0); M(i.Checked ? 1UL : 0); I(i.NursedUntil); I(i.PhysicianUntil); M(i.Entry); }
        if (Plague is { } p)
        {
            I(p.Id); I(p.NameIndex); I(p.OriginRegion); I(p.StartDay); M(p.BeginEntry); M(p.Active ? 1UL : 0); I(p.Dead); I(p.Recovered);
            foreach (var (s, e) in p.Epidemics) { I(s); I(e.StartDay); I(e.EndDay); I(e.DailyPpm); I(e.PopStart); I(e.Dead); I(e.Infected); M(e.Entry); M(e.Active ? 1UL : 0); }
            foreach (var (k, c) in p.Cases) { M(k); I(c.Settlement); I(c.InfectedDay); I(c.DeathDay); I(c.EndDay); I(c.NursedUntil); I(c.PhysicianUntil); M(c.Checked ? 1UL : 0); }
            foreach (var k in p.Immune) M(k + 0x100);
            foreach (var (s, d) in p.Quarantine) { I(s + 0x200); I(d); }
            foreach (var (s, d) in p.Burned) { I(s + 0x300); I(d); }
        }
        I(Scarcity.WageMultPm); I(Scarcity.LandMultPm); I(Scarcity.PeakWorkers); I(Scarcity.PeakHouseholds); I(Scarcity.LastDay);
        var c2 = Continent;
        M(c2.Active ? 1UL : 0); I(c2.Id); I(c2.NameIndex); I(c2.Origin); I(c2.StartDay); I(c2.DeadEstimate);
        foreach (var (r, (f, u)) in c2.Infected) { I(r); I(f); I(u); }
        foreach (var (r, d) in c2.Reports) { I(r + 0x400); I(d); }
        return h;
    }
}

/// <summary>A tracked person for the day: the id and whether he is the hero (whose death is the family's).</summary>
public readonly record struct TrackedPerson(ulong Id, bool Hero);
