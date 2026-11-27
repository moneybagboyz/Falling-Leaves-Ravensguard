using Ravensguard.Sim.Core;
using Ravensguard.Sim.Soldiers;
using Ravensguard.Sim.WarSupply;

namespace Ravensguard.Sim.Armies;

/// <summary>What the Army tab warns of (P8-WS-1, war screens 3.1): pay owed over 10 days, baggage under 5 days, men leaving.</summary>
[Flags] public enum ArmyAlert : byte { None = 0, Arrears = 1, Supply = 2, Desertion = 4 }

/// <summary>One row of the Army tab: a warband, a garrison, a host or the hero's retinue. Every figure is read from a book; the row sums nothing the books do not.</summary>
public sealed record ArmyRow(string Kind, int Id, string Name, int Men, IReadOnlyList<(string Role, int Count)> Roles, long WageSeasonMites, long ArrearsMites, int Morale, int SupplyDays, int Place, string Task, ArmyAlert Alerts)
{
    public string RolesText => string.Join(", ", Roles.Select(r => r.Count + " " + r.Role));
}

/// <summary>
/// P8-WS-1: the Army tab (the Ledger's, war screens 3.1): one row per warband, garrison, host and the hero's retinue, from the army book and the supply book (and the soldiers' book for roles and the wage table). A pure read
/// at a barrier: it draws nothing, moves nothing and holds no state, so a load restores the same view. Alerts: arrears over 10 days of the roll, supply under 5 days, men leaving.
/// </summary>
public static class ArmyTabView
{
    public const int ArrearsAlertDays = 10, SupplyAlertDays = 5;
    public const string Retinue = "retinue", Warband = "warband", Garrison = "garrison", Host = "host";

    /// <summary>What the view reads (a world or a test supplies each): wages by person, place names, the supply book's accounts, the soldiers' book.</summary>
    public sealed class Sources
    {
        public required ArmyBook Armies { get; init; }
        /// <summary>The day the view is of (the baggage's days of food are against that day's need).</summary>
        public int Day { get; init; }
        public HostSupply? Supply { get; init; }
        public SoldierWorld? Soldiers { get; init; }
        /// <summary>A man's wage a day (mites).</summary>
        public Func<ulong, long> WageOf { get; init; } = _ => 0;
        public Func<int, string> FiefName { get; init; } = f => "#" + f;
    }

    public static List<ArmyRow> Rows(Sources s)
    {
        var rows = new List<ArmyRow>();
        foreach (var (id, w) in s.Armies.Warbands)
        {
            var roles = new SortedDictionary<string, int>(StringComparer.Ordinal); long roll = 0;
            foreach (var m in w.Members) { string r = s.Soldiers?.Book.Of(m)?.Role ?? "levy"; roles[r] = roles.GetValueOrDefault(r) + 1; roll += s.WageOf(m); }
            long arrDays = roll > 0 ? w.ArrearsMites / roll : 0;
            var al = ArmyAlert.None; if (arrDays > ArrearsAlertDays) al |= ArmyAlert.Arrears;
            bool garrison = w.Task == WarbandTask.Garrison;
            rows.Add(new ArmyRow(garrison ? Garrison : Warband, id, s.FiefName(w.Fief), w.Members.Count, [.. roles.Select(kv => (kv.Key, kv.Value))], roll * TimeConst.DaysPerSeason, w.ArrearsMites, w.Morale, -1,
                w.Here >= 0 ? w.Here : w.To, "am_task_" + w.Task.ToString().ToLowerInvariant(), al));
        }
        foreach (var (id, h) in s.Armies.Hosts)
        {
            var roles = new List<(string, int)>(); long wage = 0, arrears = 0; int men, supply = -1;
            if (s.Supply != null && h.Supply > 0 && s.Supply.Exists(new HostId(h.Supply)))
            {
                var a = s.Supply.Account(new HostId(h.Supply));
                foreach (var r in Enum.GetValues<TroopRole>()) { int c = a.Men[(int)r]; if (c == 0) continue; roles.Add((r.ToString().ToLowerInvariant(), c)); wage += (long)c * SupplyConfig.Wage(r) * TimeConst.DaysPerSeason; }
                if (a.Retainers > 0) roles.Add(("retainer", a.Retainers));
                men = a.Total + a.Retainers;
                foreach (long x in a.Arrears) arrears += x;
                supply = (int)(s.Supply.BaggageDaysMilli(new HostId(h.Supply), s.Day) / 1000);
            }
            else { men = h.Levies; if (men > 0) roles.Add(("levy", men)); }
            var rat = s.Armies.Rations.GetValueOrDefault(h.Supply);
            long daily = wage / TimeConst.DaysPerSeason; long arrDays = daily > 0 ? arrears / daily : 0;
            var al = ArmyAlert.None; if (arrDays > ArrearsAlertDays) al |= ArmyAlert.Arrears; if (supply >= 0 && supply < SupplyAlertDays) al |= ArmyAlert.Supply;
            if (rat is { DesertedPersons: > 0 } && (rat.HungryRun > 0 || arrears > 0)) al |= ArmyAlert.Desertion;           // the books keep no day of the last desertion: it is flagged while its causes last
            rows.Add(new ArmyRow(Host, id, s.FiefName(h.Point), men, roles, wage, arrears, rat?.Morale ?? 60, supply, h.Point, "am_host_" + h.State.ToString().ToLowerInvariant(), al));
        }
        if (s.Soldiers != null)
        {
            var mine = s.Soldiers.Book.Rows.Values.Where(r => r.Lord == SoldierDesk.HeroLord).ToList();
            if (mine.Count > 0)
            {
                var lines = WageRoll.Rows(s.Soldiers, SoldierDesk.HeroLord);
                long daily = lines.Sum(l => l.WageMites), arrears = lines.Sum(l => l.ArrearsMites);
                var al = ArmyAlert.None; if (daily > 0 && arrears / daily > ArrearsAlertDays) al |= ArmyAlert.Arrears;
                rows.Add(new ArmyRow(Retinue, 0, "", mine.Count, [.. mine.GroupBy(r => r.Role).OrderBy(g => g.Key, StringComparer.Ordinal).Select(g => (g.Key, g.Count()))], daily * TimeConst.DaysPerSeason, arrears, -1, -1, -1, "am_task_retinue", al));
            }
        }
        return rows;
    }
}
