using MessagePack;
using Ravensguard.Sim.Abstract;
using Ravensguard.Sim.People;
using Ravensguard.Sim.People.Vital;

namespace Ravensguard.Sim.Save;

/// <summary>
/// The "abstract" section: the progress of the abstract tick (last completed hour) and every vital state the people table does not already carry: the per-person
/// vital columns (status, death, pregnancy, last birth, marriage wait), the famine and outbreak trackers per settlement, the running counters and the yearly
/// ledger. Absent in saves from before the abstract tick; a loaded save without it starts everyone alive and not pregnant. Lists are in index order, so the
/// bytes are deterministic.
/// </summary>
[MessagePackObject]
public sealed class AbstractSave
{
    [Key(0)] public int Version { get; set; } = 1;
    [Key(1)] public long HourDone { get; set; }
    [Key(2)] public string SimHash { get; set; } = "";

    // person columns, one entry per person row
    [Key(3)] public PersonStatus[] Status { get; set; } = [];
    [Key(4)] public int[] DeathDay { get; set; } = [];
    [Key(5)] public DeathCause[] DeathCause { get; set; } = [];
    [Key(6)] public int[] PregnantUntilDay { get; set; } = [];
    [Key(7)] public int[] FatherOfUnborn { get; set; } = [];
    [Key(8)] public int[] LastBirthDay { get; set; } = [];
    [Key(9)] public byte[] MarriageWait { get; set; } = [];

    // per settlement trackers
    [Key(10)] public int[] FamineStreak { get; set; } = [];
    [Key(11)] public int[] QuietStreak { get; set; } = [];
    [Key(12)] public ulong[] FamineEntry { get; set; } = [];
    [Key(13)] public int[] OutbreakEndDay { get; set; } = [];
    [Key(14)] public int[] OutbreakMult { get; set; } = [];
    [Key(15)] public ulong[] OutbreakEntry { get; set; } = [];

    // counters
    [Key(16)] public long Births { get; set; }
    [Key(17)] public long Marriages { get; set; }
    [Key(18)] public long Immigrants { get; set; }
    [Key(19)] public long Emigrants { get; set; }
    [Key(20)] public long[] DeathsByCause { get; set; } = [];
    [Key(21)] public long Alive { get; set; }
    [Key(22)] public long ChronicleDay { get; set; }
    [Key(23)] public int ChronicleCount { get; set; }

    // yearly ledger, parallel arrays
    [Key(24)] public int[] YearNumber { get; set; } = [];
    [Key(25)] public long[] YearBirths { get; set; } = [];
    [Key(26)] public long[] YearDeaths { get; set; } = [];
    [Key(27)] public long[] YearMarriages { get; set; } = [];
    [Key(28)] public long[] YearImmigrants { get; set; } = [];
    [Key(29)] public long[] YearEmigrants { get; set; } = [];
    [Key(30)] public long[] YearPopulationAtStart { get; set; } = [];

    /// <summary>Tax levels, arrears, treasury ledgers and unrest (taxes spec 2); null in a save from before taxes.</summary>
    [Key(31)] public Ravensguard.Sim.Taxes.TaxSave? Taxes { get; set; }

    private static T[] Cut<T>(T[] a, int n) => a.AsSpan(0, n).ToArray();

    /// <summary>Copies the state on the calling thread (so encoding can run elsewhere).</summary>
    public static AbstractSave Capture(AbstractState s, Population pop)
    {
        var p = pop.People; int n = p.Count; var v = s.Vital;
        return new AbstractSave
        {
            HourDone = s.HourDone, SimHash = pop.ComputeSimHash(),
            Status = Cut(p.Status, n), DeathDay = Cut(p.DeathDay, n), DeathCause = Cut(p.DeathCause, n), PregnantUntilDay = Cut(p.PregnantUntilDay, n),
            FatherOfUnborn = Cut(p.FatherOfUnborn, n), LastBirthDay = Cut(p.LastBirthDay, n), MarriageWait = Cut(p.MarriageWait, n),
            FamineStreak = (int[])v.FamineStreak.Clone(), QuietStreak = (int[])v.QuietStreak.Clone(), FamineEntry = (ulong[])v.FamineEntry.Clone(),
            OutbreakEndDay = (int[])v.OutbreakEndDay.Clone(), OutbreakMult = (int[])v.OutbreakMult.Clone(), OutbreakEntry = (ulong[])v.OutbreakEntry.Clone(),
            Births = v.Births, Marriages = v.Marriages, Immigrants = v.Immigrants, Emigrants = v.Emigrants, DeathsByCause = (long[])v.DeathsByCause.Clone(), Alive = v.Alive,
            ChronicleDay = v.ChronicleDay, ChronicleCount = v.ChronicleCount,
            YearNumber = v.Years.Select(y => y.Year).ToArray(), YearBirths = v.Years.Select(y => y.Births).ToArray(), YearDeaths = v.Years.Select(y => y.Deaths).ToArray(),
            YearMarriages = v.Years.Select(y => y.Marriages).ToArray(), YearImmigrants = v.Years.Select(y => y.Immigrants).ToArray(),
            YearEmigrants = v.Years.Select(y => y.Emigrants).ToArray(), YearPopulationAtStart = v.Years.Select(y => y.PopulationAtStart).ToArray(),
            Taxes = Ravensguard.Sim.Taxes.TaxSave.Capture(s.Taxes),
        };
    }

    /// <summary>Puts the person columns into <paramref name="pop"/> and returns the tick state; throws <see cref="SaveException"/> when the section does not fit the people.</summary>
    public AbstractState Restore(Population pop)
    {
        var p = pop.People; int n = p.Count;
        try
        {
            if (Status.Length != n || DeathDay.Length != n || DeathCause.Length != n || PregnantUntilDay.Length != n || FatherOfUnborn.Length != n || LastBirthDay.Length != n || MarriageWait.Length != n)
                throw new SaveException("The abstract section does not match the population.");
            Array.Copy(Status, p.Status, n); Array.Copy(DeathDay, p.DeathDay, n); Array.Copy(DeathCause, p.DeathCause, n); Array.Copy(PregnantUntilDay, p.PregnantUntilDay, n);
            Array.Copy(FatherOfUnborn, p.FatherOfUnborn, n); Array.Copy(LastBirthDay, p.LastBirthDay, n); Array.Copy(MarriageWait, p.MarriageWait, n);
            if (SimHash.Length > 0 && !string.Equals(SimHash, pop.ComputeSimHash(), StringComparison.Ordinal)) throw new SaveException("The vital columns of the abstract section are corrupt.");
            var v = new VitalState
            {
                FamineStreak = (int[])FamineStreak.Clone(), QuietStreak = (int[])QuietStreak.Clone(), FamineEntry = (ulong[])FamineEntry.Clone(),
                OutbreakEndDay = (int[])OutbreakEndDay.Clone(), OutbreakMult = (int[])OutbreakMult.Clone(), OutbreakEntry = (ulong[])OutbreakEntry.Clone(),
                Births = Births, Marriages = Marriages, Immigrants = Immigrants, Emigrants = Emigrants, Alive = Alive, ChronicleDay = ChronicleDay, ChronicleCount = ChronicleCount,
            };
            if (DeathsByCause.Length != v.DeathsByCause.Length) throw new SaveException("The abstract section has a different death-cause table.");
            Array.Copy(DeathsByCause, v.DeathsByCause, DeathsByCause.Length);
            for (int i = 0; i < YearNumber.Length; i++)
                v.Years.Add(new YearStats { Year = YearNumber[i], Births = YearBirths[i], Deaths = YearDeaths[i], Marriages = YearMarriages[i], Immigrants = YearImmigrants[i], Emigrants = YearEmigrants[i], PopulationAtStart = YearPopulationAtStart[i] });
            v.EnsureSettlements(pop.Region.Settlements.Count);
            return new AbstractState { HourDone = HourDone, Vital = v, Taxes = Taxes?.Restore() ?? new Ravensguard.Sim.Taxes.TaxState() };
        }
        catch (Exception ex) when (ex is IndexOutOfRangeException or ArgumentException)
        {
            throw new SaveException("Corrupt 'abstract' section in save.", ex);
        }
    }
}
