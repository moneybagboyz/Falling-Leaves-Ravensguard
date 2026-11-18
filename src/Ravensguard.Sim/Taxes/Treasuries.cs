using System.Text;
using Ravensguard.Sim.Abstract;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.WorldGen.Region;

namespace Ravensguard.Sim.Taxes;

/// <summary>One season of every treasury kind, as the soak prints it: coin received and paid by transfer reason, and the balance at the roll.</summary>
public sealed record SeasonSnapshot(int Year, int Season, long[,] Income, long[,] Expense, long[] Balance, long[] LordPaid);

/// <summary>
/// The treasuries of one region (taxes spec 2.1): the realm accounts present, every fief's, the chartered towns' (settlements without a fief) and the parishes' chests.
/// Each is a purse of the ledger (so a treasury is a chest that can be raided); this class names them, finds the one a settlement pays into, and keeps the season's
/// receipts and payments of each by transfer reason through the ledger's <see cref="Ledger.Tap"/>, which sees every transfer and so every toll, tax, relief and wage. The
/// tallies live in <see cref="TaxState"/> (saved). A season is rolled at the first hour of its first day: the finished season becomes "last season" and is kept in <see cref="History"/>.
/// </summary>
public sealed class Treasuries
{
    public static readonly string[] KindNames = ["realm", "fief", "town", "parish"];
    private const ulong RealmTag = 1UL << 40;
    private readonly SimWorld _w; private readonly TaxState _s; private readonly Ledger _led;
    private readonly List<TreasuryRecord> _all = [];
    private int[] _slotOfPurse = [];
    public IReadOnlyList<TreasuryRecord> All => _all;
    public int Count => _all.Count;
    /// <summary>Per settlement: the slot its hearth tax and rents are paid into (its fief's, else its town's), its realm's slot and its parish's, -1 where none.</summary>
    public int[] PayeeSlot { get; private set; } = [];
    public int[] RealmSlotOfSettlement { get; private set; } = [];
    public int[] ParishSlot { get; private set; } = [];
    public int[] FiefSlot { get; private set; } = [];
    public int[] RealmSlotOfFief { get; private set; } = [];
    public List<SeasonSnapshot> History { get; } = [];
    public int HistoryLimit { get; set; } = 400;

    public Treasuries(SimWorld w, TaxState s)
    {
        _w = w; _s = s; _led = w.Economy.Ledger;
        var reg = w.Population.Region; int ns = reg.Settlements.Count, nf = reg.Fiefs.Count;
        PayeeSlot = new int[ns]; RealmSlotOfSettlement = new int[ns]; ParishSlot = new int[ns]; FiefSlot = new int[nf]; RealmSlotOfFief = new int[nf];
        Array.Fill(PayeeSlot, -1); Array.Fill(RealmSlotOfSettlement, -1); Array.Fill(ParishSlot, -1); Array.Fill(FiefSlot, -1); Array.Fill(RealmSlotOfFief, -1);
        // realms first (ascending history index), then fiefs, towns and parishes in index order: the slot order is the same in every run
        var realms = new SortedSet<int>();
        foreach (var f in reg.Fiefs) if (f.Realm >= 0) realms.Add(f.Realm);
        foreach (var st in reg.Settlements) if (st.Realm >= 0) realms.Add(st.Realm);
        var realmSlot = new Dictionary<int, int>();
        foreach (int r in realms)
        {
            var id = EntityId.Make(IdKind.Faction, RealmTag | (ulong)(uint)r);
            string name = reg.Realms.FirstOrDefault(x => x.HistoryIndex == r)?.Name ?? $"Realm {r}";
            realmSlot[r] = Add(TreasuryKind.Realm, id, _led.Open(PurseKind.Treasury, id), name, realm: r);
        }
        for (int f = 0; f < nf; f++)
        {
            var fi = reg.Fiefs[f];
            FiefSlot[f] = Add(TreasuryKind.Fief, fi.Id, _led.Open(PurseKind.Treasury, fi.Id), fi.Name, fief: f, settlement: fi.SeatSettlement, realm: fi.Realm);
            RealmSlotOfFief[f] = fi.Realm >= 0 ? realmSlot[fi.Realm] : -1;
        }
        for (int s2 = 0; s2 < ns; s2++)
        {
            var st = reg.Settlements[s2];
            if (st.FiefIndex < 0) PayeeSlot[s2] = Add(TreasuryKind.Town, st.Id, _led.Open(PurseKind.Settlement, st.Id), st.Name, settlement: s2, realm: st.Realm);
            else if (st.FiefIndex < nf) PayeeSlot[s2] = FiefSlot[st.FiefIndex];
            int realm = st.FiefIndex >= 0 && st.FiefIndex < nf ? reg.Fiefs[st.FiefIndex].Realm : st.Realm;
            RealmSlotOfSettlement[s2] = realm >= 0 && realmSlot.TryGetValue(realm, out int rs) ? rs : -1;
        }
        var pop = w.Population;
        for (int s2 = 0; s2 < ns; s2++)
        {
            if (pop.PriestHousehold.Length <= s2 || pop.PriestHousehold[s2] < 0) continue;
            var st = reg.Settlements[s2];
            ParishSlot[s2] = Add(TreasuryKind.Parish, st.Id, _led.Open(PurseKind.Chest, st.Id), st.Name + " parish", settlement: s2, fief: st.FiefIndex);
        }
        _s.Shape(ns, nf, _all.Count);
        _led.Tap = OnTransfer;
        RebuildSlotMap();
    }

    private int Add(TreasuryKind kind, EntityId id, PurseId purse, string name, int fief = -1, int settlement = -1, int realm = -1)
    {
        int slot = _all.Count;
        _all.Add(new TreasuryRecord { Slot = slot, Kind = kind, Id = id, Purse = purse, Name = name, Fief = fief, Settlement = settlement, Realm = realm });
        return slot;
    }

    private void RebuildSlotMap()
    {
        int max = 0; foreach (var r in _all) max = Math.Max(max, r.Purse.Index);
        _slotOfPurse = new int[max + 1]; Array.Fill(_slotOfPurse, -1);
        foreach (var r in _all) _slotOfPurse[r.Purse.Index] = r.Slot;
    }

    private void OnTransfer(PurseId from, PurseId to, TransferReason why, long mites)
    {
        int R = _s.Reasons, f = from.Index, t = to.Index, k = (int)why;
        var map = _slotOfPurse;
        if ((uint)f < (uint)map.Length) { int sl = map[f]; if (sl >= 0) _s.Expense[sl * R + k] += mites; }
        if ((uint)t < (uint)map.Length) { int sl = map[t]; if (sl >= 0) _s.Income[sl * R + k] += mites; }
    }

    public TreasuryRecord this[int slot] => _all[slot];
    public Coin Balance(int slot) => _led.Balance(_all[slot].Purse);
    public Coin Balance(TreasuryKind kind) { long t = 0; foreach (var r in _all) if (r.Kind == kind) t += _led.Balance(r.Purse).Mites; return new Coin(t); }
    public int FiefTreasury(int fief) => fief >= 0 && fief < FiefSlot.Length ? FiefSlot[fief] : -1;
    public TreasuryRecord? RealmOfFief(int fief) => fief >= 0 && fief < RealmSlotOfFief.Length && RealmSlotOfFief[fief] >= 0 ? _all[RealmSlotOfFief[fief]] : null;

    /// <summary>Coin the treasury received this season (so far) or last season, by transfer reason.</summary>
    public long IncomeOf(int slot, TransferReason why, bool lastSeason = true) => (lastSeason ? _s.LastIncome : _s.Income)[slot * _s.Reasons + (int)why];
    public long ExpenseOf(int slot, TransferReason why, bool lastSeason = true) => (lastSeason ? _s.LastExpense : _s.Expense)[slot * _s.Reasons + (int)why];
    public long IncomeTotal(int slot, bool lastSeason = true) { long t = 0; for (int k = 0; k < _s.Reasons; k++) t += (lastSeason ? _s.LastIncome : _s.Income)[slot * _s.Reasons + k]; return t; }
    public long ExpenseTotal(int slot, bool lastSeason = true) { long t = 0; for (int k = 0; k < _s.Reasons; k++) t += (lastSeason ? _s.LastExpense : _s.Expense)[slot * _s.Reasons + k]; return t; }

    /// <summary>Closes the season's ledgers on <paramref name="day"/> (once per day number): this season's tallies become last season's and are kept in <see cref="History"/>.</summary>
    public void RollSeason(int day, GameTime now)
    {
        if (_s.LastRollDay == day) return;
        int R = _s.Reasons;
        if (_s.LastRollDay >= 0 || _s.Income.Any(v => v != 0))
        {
            var inc = new long[4, R]; var exp = new long[4, R]; var bal = new long[4]; var lord = new long[4];
            foreach (var r in _all)
            {
                int k = (int)r.Kind; bal[k] += _led.Balance(r.Purse).Mites; lord[k] += _s.LordPaid[r.Slot];
                for (int j = 0; j < R; j++) { inc[k, j] += _s.Income[r.Slot * R + j]; exp[k, j] += _s.Expense[r.Slot * R + j]; }
            }
            int cur = (int)now.Season, prev = (cur + 3) % 4;
            History.Add(new SeasonSnapshot(cur == 0 ? now.Year - 1 : now.Year, prev, inc, exp, bal, lord));
            if (History.Count > HistoryLimit) History.RemoveAt(0);
        }
        Array.Copy(_s.Income, _s.LastIncome, _s.Income.Length); Array.Clear(_s.Income);
        Array.Copy(_s.Expense, _s.LastExpense, _s.Expense.Length); Array.Clear(_s.Expense);
        Array.Copy(_s.LordPaid, _s.LastLordPaid, _s.LordPaid.Length); Array.Clear(_s.LordPaid);
        _s.LastRollDay = day;
    }

    /// <summary>The ledger view of the last finished season: income by source for each treasury kind (and the biggest fiefs), as text for the soak tool's <c>--tax</c>.</summary>
    public string LastSeasonReport(int topFiefs = 3)
    {
        var sb = new StringBuilder(); int R = _s.Reasons;
        var reasons = Enum.GetValues<TransferReason>();
        sb.AppendLine("treasury income by source, last season (crowns):");
        foreach (var kind in Enum.GetValues<TreasuryKind>())
        {
            var rows = _all.Where(r => r.Kind == kind).ToList(); if (rows.Count == 0) continue;
            sb.Append($"  {KindNames[(int)kind],-6} x{rows.Count,3} bal {Balance(kind).Mites / 100.0,10:N0} in:");
            foreach (var why in reasons) { long v = rows.Sum(r => _s.LastIncome[r.Slot * R + (int)why]); if (v != 0) sb.Append($" {why} {v / 100.0:N0}"); }
            sb.Append(" out:");
            foreach (var why in reasons) { long v = rows.Sum(r => _s.LastExpense[r.Slot * R + (int)why]); if (v != 0) sb.Append($" {why} {v / 100.0:N0}"); }
            sb.AppendLine();
        }
        return sb.ToString();
    }
}
