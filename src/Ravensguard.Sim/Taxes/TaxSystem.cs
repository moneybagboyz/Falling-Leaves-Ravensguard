using Ravensguard.Content;
using Ravensguard.Sim.Abstract;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Farming;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.People;
using Ravensguard.Sim.People.Vital;
using Ravensguard.Sim.Production;
using Ravensguard.Sim.WorldGen.Region;

namespace Ravensguard.Sim.Taxes;

/// <summary>
/// Taxes of one region (taxes spec 3, Phase 2 scope; plan 3j): the hearth tax at the four levels, collected by each fief's reeve on the first day of a season and shared with
/// the realm, dwelling rent, the arrears ladder (carry over, debt after three seasons, eviction after six, labour on the demesne works arrears off), evasion and unrest with
/// their ladder, the parishes' stipends, the lord's share of a treasury's surplus, the crown's outlay and the levy count (data only). Every payment is a ledger transfer, so
/// coin is conserved; every random choice is a counter draw (stream 0x260). Tolls are <see cref="Tolls"/>, tithe and share rents are in kind and live in farming.
/// </summary>
public sealed class TaxSystem
{
    /// <summary>Counter-RNG stream of evasion and riot rolls (taxes spec 5).</summary>
    public const uint Stream = RngStreams.TaxEvents;
    public const int CatchUpDayOfSeason = 8, TopDaysForRevolt = 30, QuietDaysToStepDown = 15, StepDownMargin = 50;

    private readonly SimWorld _w; private readonly FarmingSystem _farming; private readonly ProductionSystem _prod;
    public TaxPolicyDef Policy { get; set; }
    public TaxState State { get; }
    public Treasuries Treasuries { get; }
    /// <summary>True in wartime: the levy count uses the war ratio (no war exists in Phase 2).</summary>
    public bool AtWar { get; set; }
    /// <summary>Test hook (G-T14): the order a settlement's households are visited in; null is ascending index, the rule.</summary>
    public Func<IReadOnlyList<int>, IEnumerable<int>>? HouseholdOrder { get; set; }
    /// <summary>P6-C3 (rule-and-income 4.8): told after each settlement's round with the hearth tax it brought in (settlement, mites, the day), so the reeve's or bailiff's hands can be seen on it; null when no rule book is installed. Not saved.</summary>
    public Action<int, long, GameTime, RegionOutbox>? Collected { get; set; }
    /// <summary>The first evictions of the run (diagnostics, not saved).</summary>
    public List<string> EvictedSample { get; } = [];

    private List<int>[] _bySettlement = [];
    private Dictionary<int, long> _parcelArrears = [];
    private Dictionary<int, List<PurseId>> _boxesOf = [];
    private long _cacheHour = long.MinValue;     // the lists below are rebuilt every hour they are used, never carried over: a resumed world must see what the uninterrupted one saw

    public TaxSystem(SimWorld w, EconomyWorld ew, FarmingSystem farming, ProductionSystem prod, TaxPolicyDef policy)
    {
        _w = w; _farming = farming; _prod = prod; Policy = policy;
        State = w.State.Taxes;
        Treasuries = new Treasuries(w, State);
    }

    // ------------------------------------------------------------------ amounts

    public int HearthMites(TaxLevel level) => level switch { TaxLevel.Low => Policy.HearthLow, TaxLevel.High => Policy.HearthHigh, TaxLevel.Crushing => Policy.HearthCrushing, _ => Policy.HearthNormal };

    public int DwellingRent(DwellingType d) => d switch { DwellingType.Cottage => Policy.DwellingRentCottage, DwellingType.Longhouse => Policy.DwellingRentLonghouse, DwellingType.Burgage => Policy.DwellingRentBurgage, _ => 0 };

    public TaxLevel LevelOfFief(int fief) => State.Level(fief);
    public void SetLevel(int fief, TaxLevel level) { if (fief >= 0 && fief < State.FiefLevel.Length) State.FiefLevel[fief] = (byte)level; }
    public void SetAllLevels(TaxLevel level) { for (int f = 0; f < State.FiefLevel.Length; f++) State.FiefLevel[f] = (byte)level; }

    /// <summary>Hearth tax a household owes a season at a level, ignoring arrears and waivers (G-T1).</summary>
    public long SeasonHearthDue(TaxLevel level) => (long)HearthMites(level) * HouseholdsWithMembers();

    public int HouseholdsWithMembers() { var h = _w.Households; int n = 0; for (int i = 0; i < h.Count; i++) if (h.MemberCount[i] > 0) n++; return n; }

    // ------------------------------------------------------------------ pure rules (golden tests)

    /// <summary>The lord's share of a treasury's surplus (taxes spec 3.6, G-T12): <c>pct x (balance - one season of expenses - reserve)</c>, the reserve being <paramref name="reserveSeasons"/> seasons of expenses.</summary>
    public static long LordShare(long balance, long expenseSeason, int reserveSeasons, int pct)
    {
        long surplus = balance - expenseSeason - reserveSeasons * expenseSeason;
        return surplus <= 0 ? 0 : surplus * pct / 100;
    }

    /// <summary>The weight of one household in the unrest of its settlement, in half units (taxes spec 3.8): hunger 3, tax 2, each of the others 1, at most 3.</summary>
    public static int GrievanceWeightHalves(Grievance g)
    {
        if (g == Grievance.None) return 0;
        int w = 0;
        if ((g & Grievance.Hunger) != 0) w += 3;
        if ((g & Grievance.Tax) != 0) w += 2;
        if ((g & Grievance.Crowding) != 0) w += 1;
        if ((g & Grievance.Cold) != 0) w += 1;
        if ((g & Grievance.Crime) != 0) w += 1;
        if ((g & Grievance.Lord) != 0) w += 1;
        return Math.Min(3, w);
    }

    /// <summary>Unrest 0..1000 of a settlement from the summed weights (half units) of its <paramref name="households"/> (G-T8: 125 half units over 100 households is 625).</summary>
    public static int UnrestOf(long sumHalves, int households)
    {
        if (households <= 0) return 0;
        int n = Math.Max(households, MinUnrestHouseholds);      // under ten households there is no crowd: a lone farmstead's grievance is not a riot (decision T9)
        return (int)Math.Min(1000, (500 * sumHalves + n / 2) / n);
    }
    public const int MinUnrestHouseholds = 10;

    /// <summary>The levy count of a place (taxes spec 3.9): one man in ten households in peace, one in five at war (data only in Phase 2).</summary>
    public int LevyQuota(int households, bool atWar) => households / Math.Max(1, atWar ? Policy.LevyWarHouseholds : Policy.LevyPeaceHouseholds);

    /// <summary>The rung (0 none, 1 rumours, 2 evasion, 3 riot, 4 revolt brewing) a settlement's unrest reaches, before the 30-day test of the top rung.</summary>
    public int RungFor(int unrest) => unrest >= Policy.Rung4 ? 4 : unrest >= Policy.Rung3 ? 3 : unrest >= Policy.Rung2 ? 2 : unrest >= Policy.Rung1 ? 1 : 0;
    private int Threshold(int rung) => rung switch { 1 => Policy.Rung1, 2 => Policy.Rung2, 3 => Policy.Rung3, _ => Policy.Rung4 };

    // ------------------------------------------------------------------ caches

    private void EnsureCaches(long hour)
    {
        if (_cacheHour == hour && _bySettlement.Length == _w.Population.Region.Settlements.Count) return;
        _cacheHour = hour;
        int ns = _w.Population.Region.Settlements.Count; var hht = _w.Households;
        _bySettlement = new List<int>[ns]; for (int s = 0; s < ns; s++) _bySettlement[s] = [];
        for (int h = 0; h < hht.Count; h++) if (hht.MemberCount[h] > 0) _bySettlement[hht.Settlement[h]].Add(h);
        _boxesOf = [];
        foreach (var b in _prod.Businesses) if (b.State == BusinessState.Open && b.OwnerHousehold >= 0 && _prod.KindOf(b).Id != "manor") { if (!_boxesOf.TryGetValue(b.OwnerHousehold, out var l)) _boxesOf[b.OwnerHousehold] = l = []; l.Add(b.Purse); }
        _parcelArrears = [];
        var P = _farming.State.Parcels;
        for (int p = 0; p < P.Count; p++)
            if (P.CoinArrears[p] > 0 && P.Holder[p] >= 0) _parcelArrears[P.Holder[p]] = _parcelArrears.GetValueOrDefault(P.Holder[p]) + P.CoinArrears[p];
    }

    // ------------------------------------------------------------------ S4: the collection

    /// <summary>
    /// The reeve's round of settlement <paramref name="s"/> (spec 3.1, 3.2, 3.5), households in ascending index: hearth tax and the rent of a rented dwelling to the fief's
    /// (or the town's) treasury, then the ladder; the realm's share of the hearth receipts follows in the same step. Households in want owe half and are left their food money.
    /// </summary>
    public void CollectSettlement(int s, GameTime now, RegionOutbox ob)
    {
        var reg = _w.Population.Region; if ((uint)s >= (uint)reg.Settlements.Count) return;
        int slot = Treasuries.PayeeSlot[s]; if (slot < 0) return;
        int day = (int)now.TotalDays; EnsureCaches(now.TotalHours);
        var st = reg.Settlements[s]; var dest = Treasuries[slot].Purse; var level = State.Level(st.FiefIndex);
        bool evasionOn = State.Rung[s] >= 2; long hearthIn = 0; int evaders = 0; string causes = "";
        foreach (int h in HouseholdOrder != null ? HouseholdOrder(_bySettlement[s]) : _bySettlement[s])
            hearthIn += CollectHousehold(h, s, day, dest, slot, level, evasionOn, now, ob, ref evaders);
        int realm = Treasuries.RealmSlotOfSettlement[s];
        if (realm >= 0 && hearthIn > 0 && realm != slot)
        {
            long fiefPart = hearthIn * (100 - Policy.CrownSharePct) / 100, forward = hearthIn - fiefPart;       // the fief keeps the floor, the realm the remainder: never a mite lost
            if (forward > 0 && _w.Economy.Ledger.Transfer(dest, Treasuries[realm].Purse, new Coin(forward), TransferReason.Hearth)) State.Add(TaxTally.CrownForwarded, forward);
        }
        if (evaders > 0 && State.LastEvasionDay[s] != day)
        {
            State.LastEvasionDay[s] = day; State.Add(TaxTally.EvasionEvents, 1);
            causes = $"{evaders} households withheld the hearth tax while grievances ran high";
            ob.Chronicle(now, "tax_evasion", [st.Id], [new Cause(CauseKind.Tax, null, causes)], ("settlement", st.Name), ("households", evaders.ToString()), ("unrest", State.Unrest[s].ToString()));
        }
        Collected?.Invoke(s, hearthIn, now, ob);
    }

    private long CollectHousehold(int h, int s, int day, PurseId dest, int slot, TaxLevel level, bool evasionOn, GameTime now, RegionOutbox ob, ref int evaders)
    {
        var hht = _w.Households; var e = _w.Economy; var led = e.Ledger;
        if (hht.MemberCount[h] == 0) return 0;
        var purse = e.HouseholdPurse[h]; var row = State.Row(h); var gr = e.Grievances[h];
        bool hungry = (gr & Grievance.Hunger) != 0;
        long dueH = HearthMites(level);
        if (hungry && level != TaxLevel.Crushing) { long half = dueH / 2; State.Add(TaxTally.HearthWaived, dueH - half); State.Add(TaxTally.Waivers, 1); dueH = half; }
        long dueD = hht.Deed[h] == DeedKind.Rented ? DwellingRent(hht.Dwelling[h]) : 0;
        State.Add(TaxTally.HearthDue, dueH);
        long owedH = (row?.ArrHearth ?? 0) + dueH, owedD = (row?.ArrDwelling ?? 0) + dueD;
        bool evade = evasionOn && (gr & Grievance.Tax) != 0 && owedH + owedD > 0
            && DetRng.Range(_w.Seed, day, hht.IdOf(h).Value, Stream, 0, 0, 100) < Policy.EvasionChancePctWithGrievance;
        long avail = Math.Max(0, led.Balance(purse).Mites - (hungry ? (long)hht.MemberCount[h] * Policy.HungerKeepMitesPerMember : 0));
        long payH = 0, payD = 0;
        if (evade) { evaders++; State.Add(TaxTally.HearthEvaded, owedH + owedD); }
        else
        {
            payH = Math.Min(avail, owedH);
            if (payH > 0 && led.Transfer(purse, dest, new Coin(payH), TransferReason.Hearth)) avail -= payH; else payH = 0;
            payD = Math.Min(avail, owedD);
            if (payD > 0 && led.Transfer(purse, dest, new Coin(payD), TransferReason.Rent)) avail -= payD; else payD = 0;
            // a master whose coin is in his workshop's cash box pays the rest of the dues from it, up to half the box (spec 7: the reeve takes what the household has)
            _boxesOf.TryGetValue(h, out var boxes);
            if (payH < owedH && boxes != null) payH += FromBoxes(boxes, owedH - payH, dest, TransferReason.Hearth);
            if (payD < owedD && boxes != null) payD += FromBoxes(boxes, owedD - payD, dest, TransferReason.Rent);
        }
        State.Add(TaxTally.HearthCollected, payH); State.Add(TaxTally.DwellingCollected, payD);
        long newH = owedH - payH, newD = owedD - payD;
        if (row == null && newH == 0 && newD == 0) return payH;
        row ??= State.RowFor(h);
        row.ArrHearth = newH; row.ArrDwelling = newD;
        // a debt is paid down from what is left once the dues are met, at most half of it
        if (row.Debt > 0 && newH + newD == 0 && avail > 1)
        {
            long half = avail / 2, p1 = Math.Min(half, row.DebtHearth);
            if (p1 > 0 && led.Transfer(purse, dest, new Coin(p1), TransferReason.Hearth)) { row.DebtHearth -= p1; half -= p1; State.Add(TaxTally.DebtRepaid, p1); payH += p1; }
            long p2 = Math.Min(half, row.DebtDwelling);
            if (p2 > 0 && led.Transfer(purse, dest, new Coin(p2), TransferReason.Rent)) { row.DebtDwelling -= p2; State.Add(TaxTally.DebtRepaid, p2); }
        }
        var deed = hht.Deed[h];
        if ((deed == DeedKind.Rented || deed == DeedKind.Tenant) && row.Total > 0) { int adults = WorkingAdults(h, day); if (adults > 0) WorkOff(row, adults); }
        // a coin parcel rent stays on the lease (farming's CoinArrears) and is not a season of the ladder (decision T11): it falls due once a year, before the harvest is sold, and a tenant
        // who cannot pay it then is the rule, not the exception (it breeds the tax grievance, below)
        if (row.Arrears > 0) row.Seasons = Math.Min(row.Seasons + 1, 200); else row.Seasons = 0;
        if (row.Seasons == Policy.ArrearsDebtSeasons && row.Arrears > 0)
        {
            row.DebtHearth += row.ArrHearth; row.DebtDwelling += row.ArrDwelling; row.ArrHearth = row.ArrDwelling = 0; State.Add(TaxTally.Converted, 1);
        }
        if (row.Seasons >= Policy.ArrearsEvictSeasons && (deed == DeedKind.Rented || deed == DeedKind.Tenant))
        {
            if (WorkingAdults(h, day) == 0) { State.Add(TaxTally.WrittenOff, row.Total); row.ArrHearth = row.ArrDwelling = row.DebtHearth = row.DebtDwelling = 0; row.Seasons = 0; }   // the old and the orphaned are let stay on charity (decision T13)
            else payH += Evict(h, s, row, dest, slot, now, ob);
        }
        else if (row.IsClear) State.Arrears.Remove(h);
        return payH;
    }

    private long FromBoxes(List<PurseId> boxes, long need, PurseId dest, TransferReason why)
    {
        var led = _w.Economy.Ledger; long got = 0;
        foreach (var bp in boxes)
        {
            if (got >= need) break;
            long take = Math.Min(need - got, led.Balance(bp).Mites / 2);
            if (take > 0 && led.Transfer(bp, dest, new Coin(take), why)) got += take;
        }
        return got;
    }

    /// <summary>
    /// The reeve's second round, on the eighth day of a season (decision T12): households that could not pay on the first day, when their coin was out in wages and purchases, pay
    /// their arrears from what they hold now (hearth first, the realm's share follows), and one that clears them starts its count of unpaid seasons again. Rows of households
    /// that no longer exist are written off.
    /// </summary>
    public void CatchUp(GameTime now)
    {
        if (State.Arrears.Count == 0) return;
        int day = (int)now.TotalDays; EnsureCaches(now.TotalHours);
        var hht = _w.Households; var e = _w.Economy; var led = e.Ledger; var reg = _w.Population.Region;
        foreach (int h in State.Arrears.Keys.OrderBy(k => k).ToList())
        {
            var row = State.Arrears[h];
            if (h >= hht.Count || hht.MemberCount[h] == 0) { State.Arrears.Remove(h); continue; }
            if (row.Arrears == 0) continue;
            int s = hht.Settlement[h], slot = Treasuries.PayeeSlot[s]; if (slot < 0) continue;
            var dest = Treasuries[slot].Purse; var purse = e.HouseholdPurse[h];
            bool hungry = (e.Grievances[h] & Grievance.Hunger) != 0;
            long avail = Math.Max(0, led.Balance(purse).Mites - (hungry ? (long)hht.MemberCount[h] * Policy.HungerKeepMitesPerMember : 0));
            long payH = Math.Min(avail, row.ArrHearth);
            if (payH > 0 && led.Transfer(purse, dest, new Coin(payH), TransferReason.Hearth)) { avail -= payH; row.ArrHearth -= payH; } else payH = 0;
            long payD = Math.Min(avail, row.ArrDwelling);
            if (payD > 0 && led.Transfer(purse, dest, new Coin(payD), TransferReason.Rent)) { avail -= payD; row.ArrDwelling -= payD; } else payD = 0;
            _boxesOf.TryGetValue(h, out var boxes);
            if (row.ArrHearth > 0 && boxes != null) { long g = FromBoxes(boxes, row.ArrHearth, dest, TransferReason.Hearth); row.ArrHearth -= g; payH += g; }
            if (row.ArrDwelling > 0 && boxes != null) { long g = FromBoxes(boxes, row.ArrDwelling, dest, TransferReason.Rent); row.ArrDwelling -= g; payD += g; }
            State.Add(TaxTally.HearthCollected, payH); State.Add(TaxTally.DwellingCollected, payD); State.Add(TaxTally.LatePaid, payH + payD);
            int realm = Treasuries.RealmSlotOfSettlement[s];
            if (realm >= 0 && realm != slot && payH > 0)
            {
                long forward = payH - payH * (100 - Policy.CrownSharePct) / 100;
                if (forward > 0 && led.Transfer(dest, Treasuries[realm].Purse, new Coin(forward), TransferReason.Hearth)) State.Add(TaxTally.CrownForwarded, forward);
            }
            if (row.Arrears == 0) row.Seasons = 0;
            if (row.IsClear) State.Arrears.Remove(h);
        }
    }

    /// <summary>Labour on the demesne works arrears off (spec 3.5): up to <c>work_off_days_per_season</c> worker-days for each of two adults at <c>work_off_mites_per_day</c> (2 worker-days per 100 mites), debt first.</summary>
    private void WorkOff(ArrearsRow row, int adults)
    {
        long credit = (long)Math.Min(adults, 2) * Policy.WorkOffDaysPerSeason * Policy.WorkOffMitesPerDay, used = 0;
        long t = Math.Min(credit, row.DebtHearth); row.DebtHearth -= t; credit -= t; used += t;
        t = Math.Min(credit, row.DebtDwelling); row.DebtDwelling -= t; credit -= t; used += t;
        t = Math.Min(credit, row.ArrHearth); row.ArrHearth -= t; credit -= t; used += t;
        t = Math.Min(credit, row.ArrDwelling); row.ArrDwelling -= t; credit -= t; used += t;
        State.Add(TaxTally.WorkedOff, used);
    }

    private int WorkingAdults(int h, int day)
    {
        var p = _w.People; int n = 0;
        foreach (int m in _w.Households.Members(h)) { if (p.Status[m] != PersonStatus.Alive) continue; int age = p.AgeYears(m, day); if (age >= 16 && age < 60) n++; }
        return n;
    }

    /// <summary>
    /// Eviction (spec 3.5, O11: everyone): the creditor claims the household's coin up to what it owes, then the household leaves with the rest, its parcels fall back to the
    /// lord and its dwelling is empty for the vacancy list (the region's emigration of a whole household). The chronicle names the household and the creditor.
    /// </summary>
    private long Evict(int h, int s, ArrearsRow row, PurseId dest, int slot, GameTime now, RegionOutbox ob)
    {
        var hht = _w.Households; var led = _w.Economy.Ledger; var purse = _w.Economy.HouseholdPurse[h];
        long owed = row.Total, claim = Math.Min(owed, led.Balance(purse).Mites), seasons = row.Seasons, got = 0;
        if (claim > 0)
        {
            long a = Math.Min(claim, row.ArrHearth + row.DebtHearth);
            if (a > 0 && led.Transfer(purse, dest, new Coin(a), TransferReason.Hearth)) got += a;
            long b = Math.Min(claim - got, row.ArrDwelling + row.DebtDwelling);
            if (b > 0 && led.Transfer(purse, dest, new Coin(b), TransferReason.Rent)) got += b;
        }
        var reg = _w.Population.Region;
        ob.Chronicle(now, "eviction", [hht.IdOf(h), reg.Settlements[s].Id], [new Cause(CauseKind.Tax, null, $"evicted for arrears of {seasons} seasons")],
            ("household", hht.Name[h]), ("owed", owed.ToString()), ("claimed", got.ToString()), ("creditor", Treasuries[slot].Name));
        State.Add(TaxTally.Evictions, 1); State.Add(TaxTally.EvictedClaim, got);
        if (EvictedSample.Count < 12) EvictedSample.Add($"hh{h} {hht.Class[h]} {hht.Deed[h]} {hht.Dwelling[h]} members {hht.MemberCount[h]} adults {WorkingAdults(h, (int)now.TotalDays)} owed {owed} claim {got} in {reg.Settlements[s].Name} y{now.Year}");
        Demography.DepartHousehold(_w, h, now, ob);
        State.Arrears.Remove(h);
        return 0;
    }

    // ------------------------------------------------------------------ H5 daily: grievances, unrest, the ladder, riots, parish stipends

    /// <summary>The day's grievance bit of tax (spec 3.8) for a household: a crushing level, arrears of two seasons, an eviction notice pending, or dues over the burden share of its income.</summary>
    private bool TaxGrievance(int h, int fief, TaxLevel level, ArrearsRow? row)
    {
        var hht = _w.Households;
        if (level == TaxLevel.Crushing || _parcelArrears.ContainsKey(h)) return true;       // a coin rent behind is a grievance too
        if (row != null && (row.Seasons >= 2 || (row.Seasons >= Policy.ArrearsEvictSeasons - 1 && hht.Deed[h] != DeedKind.Owned))) return true;
        long dues = HearthMites(level) + (hht.Deed[h] == DeedKind.Rented ? DwellingRent(hht.Dwelling[h]) : 0);
        return dues * 1000 > (long)Policy.TaxBurdenGrievancePermille * hht.MemberCount[h] * Policy.IncomePerMemberSeason;
    }

    public void Daily(GameTime now, RegionOutbox ob)
    {
        var reg = _w.Population.Region; var hht = _w.Households; var e = _w.Economy; int ns = reg.Settlements.Count, nf = reg.Fiefs.Count; int day = (int)now.TotalDays;
        EnsureCaches(now.TotalHours);
        var sum = new long[ns]; var cnt = new int[ns]; var fsum = new long[nf]; var fcnt = new int[nf];
        for (int h = 0; h < hht.Count; h++)
        {
            if (hht.MemberCount[h] == 0) continue;
            int s = hht.Settlement[h], f = reg.Settlements[s].FiefIndex;
            var level = State.Level(f); var row = State.Row(h);
            var g = e.Grievances[h];
            g = TaxGrievance(h, f, level, row) ? g | Grievance.Tax : g & ~Grievance.Tax;
            e.Grievances[h] = g;
            int w = GrievanceWeightHalves(g);
            sum[s] += w; cnt[s]++;
            if (f >= 0 && f < nf) { fsum[f] += w; fcnt[f]++; }
        }
        for (int f = 0; f < nf; f++) State.FiefUnrest[f] = (short)UnrestOf(fsum[f], fcnt[f]);
        for (int s = 0; s < ns; s++)
        {
            int u = UnrestOf(sum[s], cnt[s]); State.Unrest[s] = (short)u;
            if (u >= Policy.Rung4) State.TopDays[s] = (short)Math.Min(short.MaxValue - 1, State.TopDays[s] + 1); else State.TopDays[s] = 0;
            int target = RungFor(u); if (target == 4 && State.TopDays[s] < TopDaysForRevolt) target = 3;
            int cur = State.Rung[s];
            if (target > cur)
            {
                State.Rung[s] = (byte)target; State.QuietDays[s] = 0;
                if (target == 4)
                {
                    State.Add(TaxTally.RevoltsBrewing, 1);
                    ob.Chronicle(now, "revolt_brewing", [reg.Settlements[s].Id], [new Cause(CauseKind.Tax, null, "unrest above the top rung for a month")], ("settlement", reg.Settlements[s].Name), ("unrest", u.ToString()));
                }
            }
            else if (target < cur)
            {
                if (u < Threshold(cur) - StepDownMargin) { if (++State.QuietDays[s] >= QuietDaysToStepDown) { State.Rung[s] = (byte)(cur - 1); State.QuietDays[s] = 0; } }
                else State.QuietDays[s] = 0;
            }
            else State.QuietDays[s] = 0;
            if (State.Rung[s] >= 3 && day - State.LastRiotDay[s] >= Policy.RiotCooldownDays && cnt[s] > 0) Riot(s, day, now, ob);
        }
        ParishWages();
        if (now.DayOfSeason == CatchUpDayOfSeason) CatchUp(now);
    }

    /// <summary>
    /// A riot (spec 3.8, rung 3, at most once in 30 days): max(3, 5 percent of the households) real households with a grievance, picked by ascending hash of (day, household),
    /// storm the parish granary and carry off a share of its grain, shared out evenly. The chronicle lists the grievances as causes.
    /// </summary>
    private void Riot(int s, int day, GameTime now, RegionOutbox ob)
    {
        var hht = _w.Households; var e = _w.Economy; var reg = _w.Population.Region;
        EnsureCaches(now.TotalHours);
        var pool = new List<(ulong Key, int H)>();
        foreach (int h in _bySettlement[s]) if (hht.MemberCount[h] > 0 && e.Grievances[h] != Grievance.None) pool.Add((DetRng.Next(_w.Seed, day, hht.IdOf(h).Value, Stream, (uint)(100 + day % 1000)), h));
        if (pool.Count == 0) return;
        State.LastRiotDay[s] = day;
        int n = Math.Min(pool.Count, Math.Max(Policy.RiotMinHouseholds, _bySettlement[s].Count * Policy.RiotHouseholdPct / 100));
        var riders = pool.OrderBy(x => x.Key).ThenBy(x => x.H).Take(n).Select(x => x.H).OrderBy(x => x).ToList();
        long moved = 0;
        if (e.OtherStores.TryGetValue(FarmIds.ParishGranary(s), out var granary))
            foreach (ushort g in e.Food.Staples)
            {
                if (!e.Food.IsGrain(g)) continue;
                long take = granary.Qty(g) * Policy.RiotGranaryPct / 100; if (take <= 0) continue;
                long each = take / riders.Count, rest = take - each * riders.Count;
                for (int i = 0; i < riders.Count; i++) { long q = each + (i == 0 ? rest : 0); if (q > 0) moved += Store.MoveUpTo(granary, e.HouseholdStore[riders[i]], g, q); }
            }
        int hungry = riders.Count(h => (e.Grievances[h] & Grievance.Hunger) != 0), taxed = riders.Count(h => (e.Grievances[h] & Grievance.Tax) != 0);
        var causes = new List<Cause>();
        if (taxed > 0) causes.Add(new Cause(CauseKind.Tax, null, $"{taxed} rioters under the tax"));
        if (hungry > 0) causes.Add(new Cause(CauseKind.Famine, null, $"{hungry} rioters hungry"));
        if (causes.Count == 0) causes.Add(new Cause(CauseKind.Other, null, "unrest"));
        ob.Chronicle(now, "riot", [reg.Settlements[s].Id], causes, ("settlement", reg.Settlements[s].Name), ("rioters", riders.Count.ToString()), ("grain_g", moved.ToString()), ("unrest", State.Unrest[s].ToString()));
        State.Add(TaxTally.Riots, 1); State.Add(TaxTally.RiotGrainG, moved);
    }

    /// <summary>The parish chest pays its priest's stipend a day (spec 3.6); what it cannot pay is owed, up to 90 days, and paid when it can.</summary>
    private void ParishWages()
    {
        var pop = _w.Population; var hht = _w.Households; var e = _w.Economy; var led = e.Ledger; long cap = 90L * Policy.PriestWageMitesDay;
        foreach (var rec in Treasuries.All)
        {
            if (rec.Kind != TreasuryKind.Parish) continue;
            int s = rec.Settlement; int hh = pop.PriestHousehold[s]; if (hh < 0 || hh >= hht.Count || hht.MemberCount[hh] == 0) continue;
            long owed = Math.Min(cap, State.WageOwed[rec.Slot] + Policy.PriestWageMitesDay);
            long pay = Math.Min(owed, led.Balance(rec.Purse).Mites);
            if (pay > 0 && led.Transfer(rec.Purse, e.HouseholdPurse[hh], new Coin(pay), TransferReason.Stipend)) { owed -= pay; State.Add(TaxTally.ParishWages, pay); }
            State.WageOwed[rec.Slot] = owed;
        }
    }

    // ------------------------------------------------------------------ S4 end: parish banking, the lord's share, the crown's outlay, the levy

    public void SeasonEnd(GameTime now)
    {
        BankParishes();
        PayLords();
        CrownOutlay();
        var reg = _w.Population.Region; var hht = _w.Households;
        var hhOf = new int[reg.Fiefs.Count];
        for (int h = 0; h < hht.Count; h++) if (hht.MemberCount[h] > 0) { int f = reg.Settlements[hht.Settlement[h]].FiefIndex; if (f >= 0 && f < hhOf.Length) hhOf[f]++; }
        for (int f = 0; f < hhOf.Length; f++) State.LevyDue[f] = LevyQuota(hhOf[f], AtWar);
        for (int s = 0; s < State.Rung.Length; s++) if (State.Rung[s] >= 1) State.Add(TaxTally.RumourSeeds, 1);
    }

    /// <summary>The proceeds of the tithe sales are the parish's: the priest's household puts half of what it holds above 30 days of its food into the parish chest (reason Tithe).</summary>
    private void BankParishes()
    {
        var pop = _w.Population; var hht = _w.Households; var e = _w.Economy; var led = e.Ledger;
        foreach (var rec in Treasuries.All)
        {
            if (rec.Kind != TreasuryKind.Parish) continue;
            int hh = pop.PriestHousehold[rec.Settlement]; if (hh < 0 || hh >= hht.Count || hht.MemberCount[hh] == 0) continue;
            long keep = (long)hht.MemberCount[hh] * 30 * e.Tuning.DoleMitesPerMemberDay, over = led.Balance(e.HouseholdPurse[hh]).Mites - keep;
            long give = over / 2;
            if (give > 0 && led.Transfer(e.HouseholdPurse[hh], rec.Purse, new Coin(give), TransferReason.Tithe)) State.Add(TaxTally.ParishBanked, give);
        }
    }

    /// <summary>The wage bill a fief's treasury can expect in a season: the staff on the books of its manors at the wages of their posts, fifteen days.</summary>
    public long PayrollSeason(int fief)
    {
        var reg = _w.Population.Region; var p = _w.People; long day = 0;
        foreach (var b in _prod.Businesses)
        {
            if (b.State != BusinessState.Open || _prod.KindOf(b).Id != "manor" || b.Settlement < 0 || reg.Settlements[b.Settlement].FiefIndex != fief) continue;
            foreach (int m in b.Staff)
                day += p.Occupation[m] switch { Occupation.Steward => Policy.StewardWageMitesDay, Occupation.Reeve => Policy.ReeveWageMitesDay, Occupation.Guard => Policy.GuardWageMitesDay, _ => RecipeBook.WagePerDay(WageClass.Unskilled) };
        }
        return day * TimeConst.DaysPerSeason;
    }

    private void PayLords()
    {
        var pop = _w.Population; var hht = _w.Households; var e = _w.Economy; var led = e.Ledger; var reg = pop.Region;
        for (int f = 0; f < reg.Fiefs.Count; f++)
        {
            int slot = Treasuries.FiefSlot[f]; if (slot < 0) continue;
            int lord = f < pop.FiefLordHousehold.Length ? pop.FiefLordHousehold[f] : -1;
            if (lord < 0 || lord >= hht.Count || hht.MemberCount[lord] == 0) continue;      // no lord: the share stays in the chest until a holder exists (Phase 6)
            long last = Math.Max(0, Treasuries.ExpenseTotal(slot) - State.LastLordPaid[slot]);
            long expense = Math.Max(PayrollSeason(f), last);
            long share = LordShare(led.Balance(Treasuries[slot].Purse).Mites, expense, Policy.ReserveSeasons, Policy.LordSharePctOfSurplus);
            if (share > 0 && led.Transfer(Treasuries[slot].Purse, e.HouseholdPurse[lord], new Coin(share), TransferReason.Stipend)) { State.LordPaid[slot] += share; State.Add(TaxTally.LordStipend, share); }
        }
    }

    /// <summary>
    /// The crown's outlay (decision T10): the realm returns <c>crown_outlay_pct</c> of its chest each season to the treasuries of its fiefs as a grant towards the wages of the
    /// king's sergeants and castle guards on their manors (reason Stipend), in proportion to each fief's payroll, so the crown's share of the hearth tax goes back into the
    /// closed region's circulation instead of piling up. Phase 6 gives the crown real expenses elsewhere and ends this.
    /// </summary>
    private void CrownOutlay()
    {
        var led = _w.Economy.Ledger; var reg = _w.Population.Region;
        foreach (var rec in Treasuries.All)
        {
            if (rec.Kind != TreasuryKind.Realm) continue;
            long budget = led.Balance(rec.Purse).Mites * Policy.CrownOutlayPct / 100; if (budget <= 0) continue;
            var fiefs = new List<int>(); var weight = new List<long>();
            for (int f = 0; f < reg.Fiefs.Count; f++) if (Treasuries.RealmSlotOfFief[f] == rec.Slot) { fiefs.Add(f); weight.Add(Math.Max(1, PayrollSeason(f))); }
            if (fiefs.Count == 0) continue;
            var parts = MoneySeeder.Apportion(budget, weight, fiefs.Select(f => (ulong)f).ToList());
            for (int i = 0; i < fiefs.Count; i++)
                if (parts[i] > 0 && led.Transfer(rec.Purse, Treasuries[Treasuries.FiefSlot[fiefs[i]]].Purse, new Coin(parts[i]), TransferReason.Stipend)) State.Add(TaxTally.CrownWorks, parts[i]);
        }
    }

    // ------------------------------------------------------------------ reports

    /// <summary>Households carrying arrears or debt, and the coin they owe, for the soak: (households in arrears, in debt, owed in arrears, owed in debt, longest run of unpaid seasons).</summary>
    public (int InArrears, int InDebt, long Arrears, long Debt, int MaxSeasons) ArrearsSummary()
    {
        int a = 0, d = 0, ms = 0; long ta = 0, td = 0;
        foreach (var r in State.Arrears.Values) { if (r.Arrears > 0) { a++; ta += r.Arrears; } if (r.Debt > 0) { d++; td += r.Debt; } ms = Math.Max(ms, r.Seasons); }
        return (a, d, ta, td, ms);
    }

    /// <summary>The settlements with the most unrest, with how many households carry each grievance (soak diagnostics).</summary>
    public string TopUnrest(int n = 6)
    {
        var reg = _w.Population.Region; var hht = _w.Households; var e = _w.Economy; var rows = new List<string>();
        foreach (int s in Enumerable.Range(0, reg.Settlements.Count).OrderByDescending(x => State.Unrest[x]).ThenBy(x => x).Take(n))
        {
            int hh = 0, hun = 0, tx = 0, cold = 0, arr = 0;
            for (int h = 0; h < hht.Count; h++)
            {
                if (hht.MemberCount[h] == 0 || hht.Settlement[h] != s) continue; hh++;
                var g = e.Grievances[h]; if ((g & Grievance.Hunger) != 0) hun++; if ((g & Grievance.Tax) != 0) tx++; if ((g & Grievance.Cold) != 0) cold++; if (State.Row(h) is { Arrears: > 0 }) arr++;
            }
            rows.Add($"{reg.Settlements[s].Name} ({reg.Settlements[s].Class}) unrest {State.Unrest[s]} rung {State.Rung[s]}: {hh} households, hunger {hun}, tax {tx}, cold {cold}, in arrears {arr}");
        }
        return "  most unrest: " + string.Join("; ", rows);
    }

    /// <summary>A sample of the households in arrears with what they hold (soak diagnostics).</summary>
    public string ArrearsSample(int n = 10)
    {
        var hht = _w.Households; var e = _w.Economy; var p = _w.People; var rows = new List<string>(); int day = (int)(_w.State.HourDone / 24);
        foreach (var (h, r) in State.Arrears.OrderByDescending(kv => kv.Value.Total).ThenBy(kv => kv.Key).Take(n))
        {
            var occ = string.Join("/", hht.Members(h).Take(4).Select(m => p.Occupation[m].ToString() + p.AgeYears(m, day)));
            rows.Add($"hh{h} {hht.Class[h]} {hht.Deed[h]} {hht.Dwelling[h]} members {hht.MemberCount[h]} coin {e.Ledger.Balance(e.HouseholdPurse[h]).Mites} cover {e.FoodCoverDaysX10[h] / 10.0:0.0}d arrears {r.Arrears} debt {r.Debt} seasons {r.Seasons} [{occ}]");
        }
        return "  arrears sample: " + string.Join("; ", rows);
    }

    public string Report()
    {
        var (a, d, ta, td, ms) = ArrearsSummary(); var t = State;
        int[] rungs = new int[5]; foreach (byte r in t.Rung) rungs[Math.Min(4, (int)r)]++;
        long mean = t.Unrest.Length == 0 ? 0 : t.Unrest.Sum(x => (long)x) / t.Unrest.Length; int max = t.Unrest.Length == 0 ? 0 : t.Unrest.Max();
        return $"taxes: hearth collected {t.TallyOf(TaxTally.HearthCollected) / 100.0:N0} cr (due {t.TallyOf(TaxTally.HearthDue) / 100.0:N0}, waived {t.TallyOf(TaxTally.HearthWaived) / 100.0:N0}, evaded {t.TallyOf(TaxTally.HearthEvaded) / 100.0:N0}), dwelling rent {t.TallyOf(TaxTally.DwellingCollected) / 100.0:N0} (of both, paid late in the second round {t.TallyOf(TaxTally.LatePaid) / 100.0:N0}), crown share {t.TallyOf(TaxTally.CrownForwarded) / 100.0:N0}, crown works {t.TallyOf(TaxTally.CrownWorks) / 100.0:N0}, lord stipends {t.TallyOf(TaxTally.LordStipend) / 100.0:N0}, parish wages {t.TallyOf(TaxTally.ParishWages) / 100.0:N0}, banked {t.TallyOf(TaxTally.ParishBanked) / 100.0:N0}\n"
             + $"  arrears: {a} households owe {ta / 100.0:N0} cr, {d} in debt {td / 100.0:N0} cr, longest {ms} seasons; converted {t.TallyOf(TaxTally.Converted)}, worked off {t.TallyOf(TaxTally.WorkedOff) / 100.0:N0} cr, debt repaid {t.TallyOf(TaxTally.DebtRepaid) / 100.0:N0}, written off for the helpless {t.TallyOf(TaxTally.WrittenOff) / 100.0:N0} cr, evictions {t.TallyOf(TaxTally.Evictions)} (claimed {t.TallyOf(TaxTally.EvictedClaim) / 100.0:N0} cr)\n"
             + $"  unrest: mean {mean}, max {max}, rungs 0/1/2/3/4 = {string.Join("/", rungs)}; evasion events {t.TallyOf(TaxTally.EvasionEvents)}, riots {t.TallyOf(TaxTally.Riots)} ({t.TallyOf(TaxTally.RiotGrainG) / 1000.0:N0} kg), rumour seeds {t.TallyOf(TaxTally.RumourSeeds)}, revolts brewing {t.TallyOf(TaxTally.RevoltsBrewing)}; levy {t.LevyDue.Sum()} men in peace\n{TopUnrest()}\n{ArrearsSample()}\n  evicted: {string.Join("; ", EvictedSample)}";
    }
}

/// <summary>H6 / S4 on the first day of a season: slot the treasuries' ledgers, then each settlement's collection in the hour of its index (spec 4), and at the last hour of the day the lords' shares, the crown's outlay and the levy.</summary>
public sealed class TaxCollectionSystem : AbstractSystem
{
    public TaxCollectionSystem() : base("tax-collection", TickPhase.H6Seasonal, SystemOrder.S4Taxes) { }

    public override void Step(TickContext ctx)
    {
        if (!ctx.IsSeasonStartDay) return;
        var ew = ctx.World.Get<EconomyWorld>(); if (ew == null) return;
        var tax = ew.Taxes; int ns = ctx.World.Population.Region.Settlements.Count;
        if (ctx.HourOfDay == 0) tax.Treasuries.RollSeason((int)ctx.Day, ctx.Time);
        for (int s = ctx.HourOfDay; s < ns; s += 24) tax.CollectSettlement(s, ctx.Time, ctx.Outbox);
        if (ctx.HourOfDay == 23) tax.SeasonEnd(ctx.Time);
    }
}

/// <summary>H5, hour 0: the day's tax grievances, unrest and its ladder, riots, and the parish stipends.</summary>
public sealed class TaxDailySystem : AbstractSystem
{
    public TaxDailySystem() : base("tax-daily", TickPhase.H5DailyGlobals, SystemOrder.DailyGlobalsTaxes) { }

    public override void Step(TickContext ctx)
    {
        if (ctx.HourOfDay != 0) return;
        ctx.World.Get<EconomyWorld>()?.Taxes.Daily(ctx.Time, ctx.Outbox);
    }
}
