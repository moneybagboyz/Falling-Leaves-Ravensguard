using Ravensguard.Content;
using Ravensguard.Sim.Armies;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Production;
using Ravensguard.Sim.Rule;
using Ravensguard.Sim.Save;
using Ravensguard.Sim.Soldiers;

namespace Ravensguard.Sim.WarSupply;

/// <summary>
/// P8-WS-1: the war screens' feed from a world (war screens 3.1). Each call is a pure read at a barrier: the Army tab's and the Fief screen's war rows as Ledger rows (the screen only sorts and filters), and the Armoury
/// screen's view. Nothing here writes a book, draws a number or keeps a field, so a world loaded from a save gives the same rows.
/// </summary>
public static class WarScreens
{
    static string M(long mites) => Ravensguard.Sim.Trade.Quotes.Money(mites);

    /// <summary>The ledger with the Army tab's rows (and the Fief screen's war rows) added; the ledger as it was when the world keeps no armies.</summary>
    public static LedgerView? With(WorldState st, ContentDatabase content, LedgerView? view, int day)
    {
        if (view == null) return view;
        var rows = new List<LedgerRow>(ArmyRows(st, content, day)); rows.AddRange(FiefRows(st, content, day));
        return rows.Count == 0 ? view : view with { Rows = [.. view.Rows, .. rows], AlertCount = view.AlertCount + rows.Count(r => r.Alerts != OwnerAlert.None) };
    }

    static string FiefName(WorldState st, int f) { var fs = st.Population.Region.Fiefs; return (uint)f < (uint)fs.Count ? fs[f].Name : ""; }
    static string PlaceName(WorldState st, int s) { var ss = st.Population.Region.Settlements; return (uint)s < (uint)ss.Count ? ss[s].Name : ""; }

    public static List<ArmyRow> ArmyRowsOf(WorldState st, ContentDatabase content, int day)
    {
        if (st.Armies == null || st.HomeRecords?.Sim is not { } sim) return [];
        var aw = sim.Get<ArmyWorld>();
        var sw = st.Soldiers != null && st.Economy != null ? SoldierWiring.For(st, content) : null;
        return ArmyTabView.Rows(new ArmyTabView.Sources
        {
            Armies = st.Armies, Day = day, Supply = sim.Get<HostSupply>(), Soldiers = sw, WageOf = p => aw?.WageOf(p) ?? 0, FiefName = f => FiefName(st, f),
        });
    }

    public static List<LedgerRow> ArmyRows(WorldState st, ContentDatabase content, int day)
    {
        var rows = new List<LedgerRow>(); int serial = 0;
        foreach (var r in ArmyRowsOf(st, content, day))
        {
            string where = r.Place >= 0 ? PlaceName(st, r.Place) : "";
            string name = r.Kind == ArmyTabView.Retinue ? RuleLedger.Mark + "wr_kind_retinue" : RuleLedger.Mark + "wr_kind_" + r.Kind + "|" + r.Name;
            var al = OwnerAlert.None;
            if ((r.Alerts & ArmyAlert.Arrears) != 0) al |= OwnerAlert.ArmyArrears;
            if ((r.Alerts & ArmyAlert.Supply) != 0) al |= OwnerAlert.ArmySupply;
            if ((r.Alerts & ArmyAlert.Desertion) != 0) al |= OwnerAlert.ArmyDesertion;
            string[] cells = [name, r.Men + (r.Roles.Count > 0 ? " (" + r.RolesText + ")" : ""), M(r.WageSeasonMites), M(r.ArrearsMites), r.Morale >= 0 ? r.Morale.ToString() : "", r.SupplyDays >= 0 ? r.SupplyDays.ToString() : "", where, RuleLedger.Mark + r.Task, ""];
            rows.Add(new LedgerRow(LedgerTab.Army, new EntityId((ulong)(++serial)), cells, [serial, r.Men, r.WageSeasonMites, r.ArrearsMites, r.Morale, r.SupplyDays, r.Place, 0, (long)al], al));
        }
        return rows;
    }

    /// <summary>The hero's realm: that of the fief he lives in (or the first fief of the rule book); -1 when none.</summary>
    public static int HeroRealm(WorldState st)
    {
        if (st.Rule == null) return -1;
        var region = st.Population.Region; var pt = st.Population.People;
        int row = Ravensguard.Sim.Succession.HeroPerson.RowOf(pt, st.Hero, st.Party?.Hero ?? EntityId.None);
        int s = row >= 0 ? pt.HomeSettlement[row] : -1;
        int f = s >= 0 && s < region.Settlements.Count ? region.Settlements[s].FiefIndex : -1;
        return f >= 0 && st.Rule.Fiefs.TryGetValue(f, out var fr) ? fr.Realm : -1;
    }

    public static List<FiefWarRow> FiefWarRowsOf(WorldState st, ContentDatabase content, int day)
    {
        if (st.Armies == null || st.WarSupply == null || st.HomeRecords?.Sim is not { } sim) return [];
        int realm = HeroRealm(st); if (realm < 0) return [];
        var mint = st.Economy != null && st.Coinage != null ? CoinageWiring.For(st, content).MintOf(realm) : null;
        var ctx = st.Rule != null ? RuleWiring.Context(sim, st.Rule, RuleChron.Silent) : null;
        return FiefWarView.Rows(new FiefWarView.Sources
        {
            Realm = realm, Armies = st.Armies, Supply = st.WarSupply, Rule = st.Rule, Hosts = sim.Get<HostSupply>(), Year = day / TimeConst.DaysPerYear,
            FiefName = f => FiefName(st, f), PlaceName = s => PlaceName(st, s), MintSettlement = mint?.Settlement ?? -1, NameOf = p => ctx?.NameOf((long)p) ?? "",
        });
    }

    /// <summary>The Fief screen's war rows as Fief-tab rows (the same six columns; cells that start with the mark are text keys).</summary>
    public static List<LedgerRow> FiefRows(WorldState st, ContentDatabase content, int day)
    {
        var rows = new List<LedgerRow>(); int serial = 100_000;
        foreach (var r in FiefWarRowsOf(st, content, day))
        {
            string amount = r.Unit switch { FiefWarUnit.Men => r.Value.ToString(), FiefWarUnit.Mites => M(r.Value), FiefWarUnit.Kg => r.Value + " kg", _ => "" };
            string state = r.State.StartsWith("fw_", StringComparison.Ordinal) ? RuleLedger.Mark + r.State : r.State;
            var al = r.Alert ? OwnerAlert.WagesOwed : OwnerAlert.None;
            string[] cells = [RuleLedger.Mark + r.What, r.Who, amount, state, r.Day >= 0 ? "d" + r.Day : "", ""];
            rows.Add(new LedgerRow(LedgerTab.Fief, new EntityId((ulong)(++serial)), cells, [serial, 0, r.Value, 0, 0, (long)al], al));
        }
        return rows;
    }

    /// <summary>The armouries the hero may see (the keys of the works' armouries, ascending).</summary>
    public static List<long> ArmouryKeys(WorldState st) => st.Works == null ? [] : [.. st.Works.ArmouryStock.Keys];

    /// <summary>The Armoury screen's view of one armoury and the hero's soldiers (null when the world has no soldiers' runtime).</summary>
    public static ArmouryView? Armoury(WorldState st, ContentDatabase content, long armoury)
    {
        if (st.Economy == null || st.Soldiers == null) return null;
        var sw = SoldierWiring.For(st, content);
        var pt = st.Population.People;
        return ArmouryView.Build(sw, armoury, sw.SoldiersOf(SoldierDesk.HeroLord), st.Economy.Items, p => { int r = pt.IndexOf(new EntityId(p)); return r >= 0 ? pt.FullName(r) : ""; });
    }
}
