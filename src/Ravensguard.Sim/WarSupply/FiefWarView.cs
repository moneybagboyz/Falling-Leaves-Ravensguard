using Ravensguard.Sim.Armies;
using Ravensguard.Sim.Rule;

namespace Ravensguard.Sim.WarSupply;

/// <summary>What a Fief-screen war row measures: men, mites, kilograms of grain or a plain word.</summary>
public enum FiefWarUnit : byte { Men, Mites, Kg, Text }

/// <summary>
/// One war row of the Fief screen (P8-WS-1, war screens 3.1). <see cref="What"/> is a text key (<c>fw_row_*</c>), <see cref="Who"/> a place or person, <see cref="Value"/> the book's number in <see cref="Unit"/>, <see cref="State"/> a text key
/// or a count, <see cref="Alert"/> whether something is owing past its day.
/// </summary>
public sealed record FiefWarRow(string What, string Who, long Value, FiefWarUnit Unit, string State, int Day, bool Alert);

/// <summary>
/// P8-WS-1: the Fief screen's war rows (war screens 3.1): the levy by village (who owes, who was drawn), the purveyance tallies due, the scutage owed and paid, the realm's mint and its master, and the depots with their stocks.
/// A read of the books; it makes no sum the books do not hold and keeps no state, so a load restores the same rows.
/// </summary>
public static class FiefWarView
{
    public sealed class Sources
    {
        public required int Realm { get; init; }
        public required ArmyBook Armies { get; init; }
        public required WarSupplyBook Supply { get; init; }
        public RuleBook? Rule { get; init; }
        public HostSupply? Hosts { get; init; }
        public int Year { get; init; }
        public Func<int, string> FiefName { get; init; } = f => "#" + f;
        public Func<int, string> PlaceName { get; init; } = s => "#" + s;
        /// <summary>The realm's mint: the town it stands in (-1 none) and its master's name ("" none).</summary>
        public int MintSettlement { get; init; } = -1;
        public Func<ulong, string> NameOf { get; init; } = p => "#" + p;
    }

    public static List<FiefWarRow> Rows(Sources s)
    {
        var rows = new List<FiefWarRow>();
        // the levy: each company of the realm's hosts (who owes, who was drawn), then the villages that gave men this year
        foreach (var h in s.Armies.Hosts.Values.Where(h => h.Realm == s.Realm))
            foreach (var c in h.Companies)
                rows.Add(new FiefWarRow("fw_row_levy", s.FiefName(c.Fief), c.Men.Count, FiefWarUnit.Men, c.Asked + "/" + c.Quota + "/" + c.Evaded, c.Drawn, c.Quota > c.Men.Count && c.IsDrawn));
        foreach (var (settlement, l) in s.Supply.Levied)
            if (l.Year == s.Year) rows.Add(new FiefWarRow("fw_row_village", s.PlaceName(settlement), l.Men, FiefWarUnit.Men, "", -1, false));
        // purveyance tallies owed by the realm
        foreach (var t in s.Supply.Tallies.Values.Where(t => t.Realm == s.Realm && t.State != TallyState.Paid))
            rows.Add(new FiefWarRow("fw_row_tally", s.PlaceName(t.Settlement), t.Owing, FiefWarUnit.Mites, "fw_state_" + t.State.ToString().ToLowerInvariant(), t.DueDay, t.State == TallyState.Defaulted));
        // scutage: the dues of that kind, owed and paid
        if (s.Rule != null)
            foreach (var d in s.Rule.Dues.Where(d => d.Kind == DueKind.Scutage && d.State != DueState.Waived))
                rows.Add(new FiefWarRow("fw_row_scutage", d.Payer.Kind == PartyKind.Fief ? s.FiefName((int)d.Payer.Id) : "", d.AmountMites, FiefWarUnit.Mites, d.PaidMites.ToString(), d.DueDay, d.State is DueState.Arrears or DueState.Grievance));
        // the mint and its master
        if (s.MintSettlement >= 0)
        {
            var master = s.Rule?.OfficeOfRealm(OfficeKind.MintMaster, s.Realm);
            rows.Add(new FiefWarRow("fw_row_mint", s.PlaceName(s.MintSettlement), 0, FiefWarUnit.Text, master != null ? s.NameOf((ulong)master.Holder) : "fw_state_unstaffed", -1, master == null));
        }
        // depots of the realm's hosts and what they hold (kilograms)
        if (s.Hosts != null)
        {
            var seen = new SortedSet<int>();
            foreach (var a in s.Supply.Hosts.Values.Where(a => a.Realm == s.Realm)) foreach (int d in a.Depots) seen.Add(d);
            foreach (int d in seen)
                rows.Add(new FiefWarRow("fw_row_depot", s.PlaceName(d), (s.Hosts.DepotStore(d)?.TotalMassG() ?? 0) / 1000, FiefWarUnit.Kg, "", -1, false));
        }
        return rows;
    }
}
