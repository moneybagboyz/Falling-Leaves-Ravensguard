using System.Security.Cryptography;
using System.Text;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Travel;

namespace Ravensguard.Sim.Autopilot;

/// <summary>
/// All the autopilot keeps for a world (autopilot spec 3.1): one <see cref="AutopilotState"/> per person that has ever had autopilot (the hero and up to 12 party members), the log, the router and the counters. One object, one
/// save section ("autopilot"), advanced in game seconds only, so it gives the same results at 1x and 8x. Absent from a world nobody turned autopilot on in, so older saves keep their bytes.
/// </summary>
public sealed class AutopilotBook
{
    public const int MaxPersons = 13;
    public SortedDictionary<EntityId, AutopilotState> States { get; } = [];
    public AutopilotLog Log { get; } = new();
    public InterruptRouter Router { get; }
    /// <summary>Index in the population of the row the hero's days are modelled on (the NPC the hero "stands in for"); -1: picked on first use.</summary>
    public int StandIn { get; set; } = -1;
    public long SummariesMade { get; set; }
    public long BigChoicesDeclined { get; set; }
    public long ContractsAccepted { get; set; }
    public long ContractsDeclined { get; set; }
    /// <summary>The number of times stream 0x380 was drawn: always zero in v1 (a test asserts it).</summary>
    public long ReservedDraws => 0;
    private readonly Dictionary<EntityId, InterruptBus> _ownBuses = [];

    public AutopilotBook() { Router = new InterruptRouter(Log); }

    public bool IsEmpty => States.Count == 0 && Log.Added == 0;

    public AutopilotState? Get(EntityId person) => States.GetValueOrDefault(person);

    /// <summary>The row of a person, made with the defaults of open question 1 (3 crowns a day, no crimes, risk Medium) the first time.</summary>
    public AutopilotState GetOrAdd(EntityId person, AutopilotContent cfg)
    {
        if (States.TryGetValue(person, out var st)) return st;
        if (States.Count >= MaxPersons) throw new InvalidOperationException("autopilot tracks at most 13 persons");
        st = new AutopilotState { Person = person };
        st.Limits.SpendCapMitesPerDay = cfg.Settings.DefaultSpendCapMitesPerDay; st.Limits.PurseReserveMites = cfg.Settings.DefaultPurseReserveMites; st.Limits.MaxRiskPm = cfg.Settings.DefaultMaxRiskPm;
        States[person] = st;
        return st;
    }

    /// <summary>The bus a person's interrupts go through: the hero's is travel's (the HUD and the clock read it), a party member's is private.</summary>
    public InterruptBus BusOf(EntityId person, InterruptBus? heroBus, EntityId hero)
    {
        if (person == hero && heroBus != null) return heroBus;
        if (!_ownBuses.TryGetValue(person, out var b)) _ownBuses[person] = b = new InterruptBus { Autopilot = true };
        return b;
    }

    /// <summary>A short, stable fingerprint of everything the autopilot keeps (tests compare runs by it).</summary>
    public string ComputeHash()
    {
        var sb = new StringBuilder();
        foreach (var (id, s) in States)
        {
            sb.Append(id.Value).Append(':').Append(s.Enabled).Append(s.Paused).Append(s.ActiveIdx).Append(',').Append(s.NextWindow).Append(',').Append((int)s.Intent).Append(',').Append(s.SpentDay).Append(',').Append(s.SpentToday).Append(',')
              .Append(s.SpentTotal).Append(',').Append(s.PosX).Append(',').Append(s.PosY).Append(',').Append(s.LastMealSec).Append(',').Append(s.NutritionCd).Append(',').Append(s.HealthPm).Append(';');
            foreach (var d in s.Queue) sb.Append((int)d.Kind).Append('/').Append((int)d.State).Append('/').Append(d.Aux0).Append('/').Append(d.Aux1).Append('|');
            foreach (var m in s.Summaries) sb.Append(m.Day).Append(':').Append(m.Earned).Append(':').Append(m.Spent).Append(':').Append(m.JobsDone).Append('|');
        }
        sb.Append(Log.Added).Append(',').Append(Log.Violations).Append(',').Append(SummariesMade);
        foreach (var l in Log.Lines) sb.Append(l.Sec).Append(l.Code).Append(l.Arg).Append(l.Text);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())))[..16];
    }
}
