using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Rule;

namespace Ravensguard.Sim.WarSupply;

/// <summary>What one vassal did when the overlord called his host: served (a Service due, his men to join) or paid scutage (a Scutage due and the coin to the overlord's treasury, as far as his treasury goes; the rest falls to the season's settling).</summary>
public sealed record ScutageCall(int Fief, long Vassal, int Men, int Days, bool Served, long Owed, long Paid);

public sealed partial class HostSupply
{
    /// <summary>
    /// The overlord calls his host (army-economy 4.4): each vassal fief of the realm owes <c>men x 40</c> days of war service, or scutage in its place, <c>men x days x 300</c> mites to the overlord's treasury (<c>Scutage</c>). An NPC vassal
    /// serves when his opinion of the overlord plus a draw of -10 to 10 (stream 0x753, key the vassal and the season) is not below zero and the fief has the households to give the men; else he pays. <paramref name="playerPays"/> answers for a fief
    /// the player holds (true: pay, false: serve; null: an NPC). What the vassal cannot pay stays a due the season's settling takes to arrears and, a season on, to a grievance. <c>scutage_paid</c> is written once a season per overlord.
    /// </summary>
    public List<ScutageCall> CallWarService(int realm, int day, Func<int, bool?>? playerPays = null)
    {
        var calls = new List<ScutageCall>(); var rb = Rules; if (rb == null) return calls;
        var c = RuleWiring.Context(Economy.World, rb, Chron); int season = day / TimeConst.DaysPerSeason;
        foreach (var f in rb.Fiefs.Values.ToList())
        {
            if (f.Realm != realm || f.Forfeited || f.Holder < 0 || f.ServiceMen <= 0) continue;
            var overlord = c.OverlordOf(f); if (overlord.IsNone) continue;
            int men = f.ServiceMen, days = SupplyConfig.WarServiceDays; var vassal = Party.OfFief(f.Fief);
            bool? choice = playerPays?.Invoke(f.Fief); bool serves;
            if (choice.HasValue) serves = !choice.Value;
            else
            {
                int opinion = rb.Opinion(c.KeyOf(vassal).Value, c.KeyOf(overlord).Value);
                int draw = DetRng.Range(c.Seed, season, (ulong)f.Holder, RngStreams.VassalServeOrPay, 0, -10, 11);
                serves = opinion + draw >= 0 && c.HouseholdsOfFief(f.Fief).Count() >= men;
            }
            Book.Stats[13] += (long)men * days;
            if (serves)
            {
                RuleDues.AddDue(c, DueKind.Service, vassal, overlord, 0, men * 1000 + days, day + 15, EntityId.None);
                calls.Add(new ScutageCall(f.Fief, f.Holder, men, days, true, 0, 0));
                continue;
            }
            long owed = (long)men * days * SupplyConfig.ScutageMitesPerManDay;
            var due = RuleDues.AddDue(c, DueKind.Scutage, vassal, overlord, owed, men, day, EntityId.None);
            long paid = c.PayUpTo(vassal, overlord, owed, TransferReason.Scutage);
            due.PaidMites += paid;
            if (due.Owing == 0) due.State = DueState.Paid;
            if (paid > 0) AddScutageNote(c.KeyOf(overlord).Value, overlord.Kind, paid);
            Book.Stats[12] += paid;
            calls.Add(new ScutageCall(f.Fief, f.Holder, men, days, false, owed, paid));
        }
        return calls;
    }

    private void AddScutageNote(ulong overlordKey, PartyKind kind, long paid)
    {
        int i = Book.ScutageNotes.FindIndex(n => n.Overlord == (long)overlordKey && n.OverlordKind == (byte)kind);
        if (i < 0) Book.ScutageNotes.Add(new ScutageNote((byte)kind, (long)overlordKey, 1, paid));
        else { var n = Book.ScutageNotes[i]; Book.ScutageNotes[i] = n with { Payers = n.Payers + 1, Total = n.Total + paid }; }
    }

    /// <summary>The season's scutage entry (<c>scutage_paid</c>, one per overlord, a root): written at S5 and cleared.</summary>
    private void NoteScutage(int day)
    {
        foreach (var n in Book.ScutageNotes)
            Chron.Chron(RuleCtx.At(day), "scutage_paid", "ch_war_scutage", [ChronParam.Count("payers", n.Payers), ChronParam.Mites("amount", n.Total)], [new EntityId((ulong)n.Overlord)], [new Cause(CauseKind.Supply, null, NoteKey: "ch_note_war_scutage")]);
        Book.ScutageNotes.Clear();
    }
}
