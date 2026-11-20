using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Production;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Taxes;
using Ravensguard.Sim.Trade;

namespace Ravensguard.Sim.Rule;

/// <summary>
/// The Ledger's Fief tab (rule-and-income 6, plan 3j): for each fief the hero holds (and, as a read of what any traveller can learn, the policy of the fief he lives in) the season's receipts by source, the arrears, the
/// grievances, each official's record against what was expected, the dues owed up and down the chain and the loans. Cells starting with <see cref="Mark"/> are text keys the screen translates. Built at a barrier from the
/// rule book and the treasuries' books; the screen only sorts and filters.
/// </summary>
public static class RuleLedger
{
    public const string Mark = "@";
    /// <summary>An official whose collections fall this far (percent) short of what was expected is an alert.</summary>
    public const int ShortfallAlertPct = 25;

    public static readonly string[] Columns = ["lg_col_what", "lg_col_who", "lg_col_amount", "lg_col_state", "lg_col_ends", "lg_col_alerts"];

    private static string M(long mites) => Quotes.Money(mites);

    /// <summary>The ledger with the Fief tab's rows added (or the ledger as it was when the world keeps no rule book).</summary>
    public static LedgerView? With(WorldState st, LedgerView? view, int day)
    {
        if (view == null || st.Rule == null || st.HomeRecords == null) return view;
        var rows = Rows(st, day);
        return rows.Count == 0 ? view : view with { Rows = [.. view.Rows, .. rows], AlertCount = view.AlertCount + rows.Count(r => r.Alerts != OwnerAlert.None) };
    }

    public static List<LedgerRow> Rows(WorldState st, int day)
    {
        var rows = new List<LedgerRow>(); var b = st.Rule!; var sim = st.HomeRecords!.Sim;
        var c = RuleWiring.Context(sim, b, RuleChron.Silent);
        long hero = (long)(st.Party?.Hero ?? EntityId.None).Value;
        int serial = 0;
        void Add(string what, string who, long amount, string state, string ends, OwnerAlert alert, long key = 0)
        {
            string[] cells = [Mark + what, who, amount != 0 || what.StartsWith("ru_row_") ? M(amount) : "", state, ends, ""];
            rows.Add(new LedgerRow(LedgerTab.Fief, new EntityId((ulong)(++serial)), cells, [serial, 0, amount, 0, 0, (long)alert], alert));
            _ = key;
        }
        var region = c.Region; var tax = c.Tax; var t = c.T;
        var held = b.Fiefs.Values.Where(f => f.Holder == hero && !f.Forfeited).Select(f => f.Fief).ToList();
        int home = HomeFief(st);
        var shown = held.Count > 0 ? held : home >= 0 ? [home] : new List<int>();
        foreach (int fief in shown)
        {
            var f = b.Fiefs[fief]; string name = c.FiefName(fief);
            bool mine = held.Contains(fief);
            if (tax != null) Add("ru_row_policy", name, tax.HearthMites(tax.LevelOfFief(fief)), Mark + "ru_level_" + tax.LevelOfFief(fief).ToString().ToLowerInvariant(), Mark + (f.RentInCoin ? "ru_rent_coin" : "ru_rent_kind"), OwnerAlert.None);
            if (!mine) continue;                                                                       // what a lord's books show: only the lord sees them
            if (t != null && fief < t.FiefSlot.Length && t.FiefSlot[fief] >= 0)
                foreach (var r in RuleDues.ReceiptReasons)
                {
                    long got = t.IncomeOf(t.FiefSlot[fief], r, lastSeason: true); if (got <= 0) continue;
                    Add("ru_src_" + r.ToString().ToLowerInvariant(), name, got, "", "", OwnerAlert.None);
                }
            int arrearsHh = 0; long arrears = 0; int grievances = 0;
            if (tax != null) foreach (int h in c.HouseholdsOfFief(fief)) { var row = tax.State.Row(h); if (row is { Total: > 0 }) { arrearsHh++; arrears += row.Total; } if (b.GrievancePoints(c.Households.IdOf(h).Value, day) > 0) grievances++; }
            Add("ru_row_arrears", name, arrears, arrearsHh.ToString(), "", arrearsHh > 0 ? OwnerAlert.WagesOwed : OwnerAlert.None);
            Add("ru_row_grievances", name, grievances, grievances.ToString(), "", OwnerAlert.None);
            foreach (var o in b.OfficesOf(fief).OrderBy(o => o.Serial))
            {
                int short_ = RuleOfficials.ShortfallPct(o); bool alert = o.Expected > 0 && short_ >= ShortfallAlertPct;
                Add("ru_office_" + o.Kind.ToString().ToLowerInvariant(), c.NameOf(o.Holder), o.Collected, short_ + "%", "d" + o.AppointedDay, alert ? OwnerAlert.Shortfall : OwnerAlert.None);
            }
        }
        foreach (var cells in Ravensguard.Sim.Unrest.UnrestLedger.Lines(st, shown, day))                          // P6-E1: the rung, the unrest and the top grievances of each restless settlement
            rows.Add(new LedgerRow(LedgerTab.Fief, new EntityId((ulong)(++serial)), cells, [serial, 0, 0, 0, 0, 0], OwnerAlert.None));
        // the dues and loans the hero (or his fief) owes up and down the chain
        var mineFiefs = held.ToHashSet(); var me = Party.OfPerson(new EntityId((ulong)hero)); var myHh = c.HouseholdOf(hero);
        bool Mine(Party p) => p == me || p == myHh || (p.Kind == PartyKind.Fief && mineFiefs.Contains((int)p.Id));
        string NameOf(Party p) => p.Kind switch { PartyKind.Fief => c.FiefName((int)p.Id), PartyKind.Realm => c.RealmName((int)p.Id), PartyKind.Household => HouseholdName(c, p), _ => c.NameOf(p.Id) };
        foreach (var d in b.Dues.OrderBy(d => d.Serial))
        {
            if (d.State is DueState.Paid or DueState.Waived || !(Mine(d.Payer) || Mine(d.Payee))) continue;
            bool owe = Mine(d.Payer);
            Add("ru_due_" + d.Kind.ToString().ToLowerInvariant(), NameOf(owe ? d.Payee : d.Payer), owe ? -d.Owing : d.Owing, Mark + "ru_state_" + d.State.ToString().ToLowerInvariant(), "d" + d.DueDay, d.State is DueState.Arrears or DueState.Grievance && owe ? OwnerAlert.WagesOwed : OwnerAlert.None);
        }
        foreach (var l in b.Loans.OrderBy(l => l.Serial))
        {
            if (l.State != LoanState.Open || !(Mine(l.Borrower) || Mine(l.Lender))) continue;
            bool owe = Mine(l.Borrower);
            Add("ru_row_loan", NameOf(owe ? l.Lender : l.Borrower), owe ? -l.Owing : l.Owing, $"{l.SeasonsPaid}/{l.TermSeasons}", "d" + (l.StartDay + l.TermSeasons * TimeConst.DaysPerSeason), l.LateSeasons > 0 && owe ? OwnerAlert.WagesOwed : OwnerAlert.None);
        }
        return rows;
    }

    private static string HouseholdName(RuleCtx c, Party p) { int h = c.Households.IndexOf(new EntityId((ulong)p.Id)); return h >= 0 ? c.Households.Name[h] : ""; }

    /// <summary>The fief the hero lives in (the settlement of his first move), -1 when he stands in none.</summary>
    private static int HomeFief(WorldState st)
    {
        var region = st.Population.Region; var pt = st.Population.People;
        int row = Ravensguard.Sim.Succession.HeroPerson.RowOf(pt, st.Hero, st.Party?.Hero ?? EntityId.None);
        int s = row >= 0 ? pt.HomeSettlement[row] : -1;
        int f = s >= 0 && s < region.Settlements.Count ? region.Settlements[s].FiefIndex : -1;
        return f >= 0 && st.Rule!.Fiefs.ContainsKey(f) ? f : st.Rule!.Fiefs.Keys.FirstOrDefault(-1);
    }
}
