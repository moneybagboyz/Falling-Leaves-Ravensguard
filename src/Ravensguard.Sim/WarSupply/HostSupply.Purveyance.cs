using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.Economy.Markets;
using Ravensguard.Sim.Farming;
using Ravensguard.Sim.Goods;
using Ravensguard.Sim.Land;
using Ravensguard.Sim.Rule;
using Ravensguard.Sim.Unrest;

namespace Ravensguard.Sim.WarSupply;

/// <summary>
/// Purveyance (army-economy 4.3, P7-AE2): the ruler's officials take the grain a household holds above thirty days of its own need within 16 km of the host, in the host's own realm, at three quarters of the book's mid price, owed by a
/// tally the realm pays at S5 when its treasury holds more than a season of its payroll and defaults four seasons past due. Each household hides grain with P = 20 + unrest / 20 per cent (stream 0x660, key household and day) and then gives
/// half. Every household taken from carries a <c>purveyance</c> grievance, a settlement at 350 unrest or more has a riot triggered, and the chronicle writes <c>purveyance_taken</c> with the host's campaign as its cause. The grain is
/// moved from real stores into the baggage and the debt is a real tally the realm's treasury pays in coin: nothing is made.
/// </summary>
public sealed partial class HostSupply
{
    /// <summary>Test hook, not saved: false switches hiding off (AE-5 forces it off).</summary>
    public bool HidingEnabled { get; set; } = true;
    /// <summary>The soak's observer, not saved: a settlement was purveyed on a day, the households taken from (ids).</summary>
    public Action<int, int, IReadOnlyList<ulong>>? OnPurveyed { get; set; }
    /// <summary>P8-WT-1, not saved: war touched a settlement (settlement, level 1 foraged, day, chronicle entry): the armies' flight roll reads it.</summary>
    public Action<int, int, int, ulong>? OnWarTouch { get; set; }

    private Ravensguard.Sim.Rule.RuleBook? Rules => Economy.World.Get<Ravensguard.Sim.Rule.RuleBook>();
    private UnrestBook? UnrestBk => Economy.World.Get<UnrestBook>();

    /// <summary>The realm a settlement pays: its fief's, else its own (the rule's reading).</summary>
    public int RealmOf(int settlement)
    {
        var region = _pop.Region; if ((uint)settlement >= (uint)region.Settlements.Count) return -1;
        var st = region.Settlements[settlement];
        return st.FiefIndex >= 0 && st.FiefIndex < region.Fiefs.Count && region.Fiefs[st.FiefIndex].Realm >= 0 ? region.Fiefs[st.FiefIndex].Realm : st.Realm;
    }

    private bool InOwnRealm(HostAccount a) => a.Realm < 0 || (a.PlaceSettlement >= 0 && RealmOf(a.PlaceSettlement) == a.Realm);

    /// <summary>Settlements whose centre is within <paramref name="radiusM"/> of the host's place, nearest first (ties by index).</summary>
    private List<int> SettlementsWithin(HostAccount a, int radiusM)
    {
        var region = _pop.Region; var list = new List<(long D2, int S)>(); long r2 = (long)radiusM * radiusM;
        for (int s = 0; s < region.Settlements.Count; s++)
        {
            long dx = region.Settlements[s].CenterX - a.PlaceXM, dy = region.Settlements[s].CenterY - a.PlaceYM, d2 = dx * dx + dy * dy;
            if (d2 <= r2) list.Add((d2, s));
        }
        list.Sort((x, y) => x.D2 != y.D2 ? x.D2.CompareTo(y.D2) : x.S.CompareTo(y.S));
        return [.. list.Select(x => x.S)];
    }

    private long GrainEq(Store s) { long t = 0; foreach (var g in _stapleGoods) { long q = s.Qty(g); if (q > 0) t += EqG(g, q); } return t; }

    /// <summary>The book's mid price in micro-mites a gram of the good at a settlement, else the mean reference price over the region's books.</summary>
    private long MidMicroAt(int settlement, ushort good)
    {
        var bk = BookOf(settlement);
        if (bk != null) { long m = bk.MidMicro(good); if (m > 0) return m; }
        long sum = 0; int n = 0;
        foreach (var b in Economy.Markets.AllBooks()) { long r = b.Goods[good].RefMicro; if (r > 0) { sum += r; n++; } }
        return n == 0 ? 0 : sum / n;
    }

    /// <summary>What <see cref="TakeGrain"/> took from a settlement in a day.</summary>
    private sealed class Taken
    {
        public long Eq, Grams, Mites, HiddenEq;
        public readonly List<int> Households = [];
        public readonly List<PurveyanceTally> Tallies = [];
    }

    private enum TakeMode : byte { Purvey, ForagePaid, ForagePlunder }

    /// <summary>
    /// Takes up to <paramref name="limitEq"/> measures of grain from the households of a settlement into the baggage (ascending household; the lord's own household is the food rent's, not touched): in the paying modes only what is above 30
    /// days of a household's own need, hiding in purveyance, a tally for each household at the king's price; in the plunder mode everything it holds and no tally. Returns what moved.
    /// </summary>
    private Taken TakeGrain(HostAccount a, Rt rt, int s, long limitEq, int day, TakeMode mode)
    {
        var t = new Taken(); var hh = _pop.Households; var unrest = UnrestBk; int unrestPm = unrest?.UnrestOf(s) ?? 0;
        int lordHh = -1;
        if (a.Lord != 0) { int p = _pop.People.IndexOf(new EntityId(a.Lord)); if (p >= 0) lordHh = _pop.People.Household[p]; }
        ulong seed = Economy.World.Seed; Int128 cum = 0; long cumMites = 0;
        for (int i = 0; i < hh.Count && t.Eq < limitEq; i++)
        {
            if (hh.Settlement[i] != s || hh.MemberCount[i] == 0 || i == lordHh) continue;
            _econ.OtherStores.TryGetValue(FarmIds.Barn(i), out var barn); var store = _econ.HouseholdStore[i];
            long have = (barn != null ? GrainEq(barn) : 0) + GrainEq(store);
            long keep = mode == TakeMode.ForagePlunder ? 0 : (long)hh.MemberCount[i] * 1100 * SupplyConfig.ReserveGrainDays;
            long surplus = have - keep; if (surplus <= 0) continue;
            if (mode == TakeMode.Purvey && HidingEnabled)
            {
                int p = SupplyConfig.HideBasePct + unrestPm / 20;
                if (DetRng.Range(seed, day, hh.IdOf(i).Value, RngStreams.PurveyanceHiding, 0, 0, 100) < p) { long give = surplus / 2; t.HiddenEq += surplus - give; surplus = give; }
            }
            long want = Math.Min(surplus, limitEq - t.Eq); if (want <= 0) continue;
            Int128 micro = 0; long eqMoved = 0, grams = 0;
            foreach (var from in new[] { barn, store })
            {
                if (from == null || want - eqMoved <= 0) continue;
                foreach (var g in _stapleGoods)
                {
                    long left = want - eqMoved; if (left <= 0) break;
                    long q = from.Qty(g); if (q <= 0) continue;
                    long take = Math.Min(q, QtyFor(SupplyNeed.Staple, g, left));
                    long eq = Math.Min(left, EqG(g, take)); if (eq <= 0) continue;
                    if (!Store.Move(from, rt.Baggage, g, take)) continue;
                    eqMoved += eq; grams += take; micro += (Int128)take * MidMicroAt(s, g);
                }
            }
            if (eqMoved <= 0) continue;
            t.Eq += eqMoved; t.Grams += grams; t.Households.Add(i);
            if (mode != TakeMode.ForagePlunder)
            {
                // the settlement's tallies add up to the whole sum at the king's price: each household's is the step of the running floor (no mite is lost to rounding)
                cum += micro * SupplyConfig.KingPricePm / 1000; long upTo = (long)(cum / PriceCurve.Micro), mites = upTo - cumMites; cumMites = upTo; if (mites <= 0) mites = 1;
                var tally = new PurveyanceTally { Id = Book.NextTally++, Realm = a.Realm, Settlement = s, Host = a.Id, TakenDay = day, DueDay = day + SupplyConfig.TallyDueDays, Household = hh.IdOf(i).Value, Mites = mites, Grams = grams, State = TallyState.Owed };
                Book.Tallies[tally.Id] = tally; t.Tallies.Add(tally); t.Mites += mites;
            }
        }
        return t;
    }

    /// <summary>Ladder step 6: purveyance for the staple. Returns what is still wanted.</summary>
    private long Purvey(HostAccount a, Rt rt, DayResult res, ushort[] goods, long left, long need, int day)
    {
        if (!a.Purveyance || a.Abstract || left <= 0 || !InOwnRealm(a)) return left;
        long bagEq = 0; foreach (var g in _stapleGoods) bagEq += EqG(g, rt.Baggage.Qty(g));
        long limit = left + Math.Max(0, need * (SupplyConfig.PurveyStockDays - 1) - bagEq);
        long got = 0;
        foreach (int s in SettlementsWithin(a, SupplyConfig.PurveyanceRadiusM))
        {
            if (got >= limit) break;
            if (a.Realm >= 0 && RealmOf(s) != a.Realm) continue;
            var t = TakeGrain(a, rt, s, limit - got, day, TakeMode.Purvey);
            Book.Stats[0] += t.Grams; Book.Stats[5] += t.HiddenEq;
            if (t.Eq <= 0) continue;
            got += t.Eq; NotePurveyance(a, res, s, t, day);
        }
        return got <= 0 ? left : Pull(rt, res, SupplyNeed.Staple, goods, DestroyReason.Eaten, left, SupplySource.Purveyance, rt.Baggage, 0);
    }

    /// <summary>The chronicle entry of a settlement's day, the tallies' cause, the grievances on the households taken from and the riot trigger.</summary>
    private void NotePurveyance(HostAccount a, DayResult res, int s, Taken t, int day)
    {
        var sid = _pop.Region.Settlements[s].Id;
        var e = Chron.Chron(WriteTime(day), "purveyance_taken", "ch_war_purveyance", [ChronParam.Place("place", sid), ChronParam.Count("kg", t.Grams / 1000), ChronParam.Count("households", t.Households.Count), ChronParam.Mites("amount", t.Mites)],
            a.Lord != 0 ? [sid, new EntityId(a.Lord)] : [sid], [HostCause(a, CauseKind.Supply, "ch_note_war_purveyance")], ("host", a.Id.ToString()));
        if (e.Value != 0) res.EventIds.Add(e);
        foreach (var tl in t.Tallies) tl.Entry = e.Value;
        Book.Stats[1] += t.Mites; Book.Stats[4] += t.Households.Count;
        OnPurveyed?.Invoke(s, day, [.. t.Households.Select(i => _pop.Households.IdOf(i).Value)]);
        var unrest = UnrestBk; if (unrest == null) return;
        foreach (int i in t.Households) unrest.Add(_pop.Households.IdOf(i).Value, "purveyance", day, e.Value);
        if (unrest.UnrestOf(s) >= SupplyConfig.PurveyRiotUnrest) UnrestConcede.Trigger(unrest, s, "tallage", day);
    }

    // ------------------------------------------------------------------ tallies at S5

    /// <summary>The realm's treasury purse, none when it has not one.</summary>
    private PurseId RealmPurse(int realm) => realm >= 0 && _econ.Ledger.TryFind(PurseKind.Treasury, LandWorld.RealmTreasuryId(realm), out var p) ? p : PurseId.None;

    /// <summary>A season of the realm's payroll: its fiefs' staff and the pay of its live hosts for a season (army-economy 4.3: the realm pays tallies while its treasury holds more than this).</summary>
    public long RealmPayrollSeason(int realm)
    {
        long sum = 0; var tax = Economy.Taxes;
        var rb = Rules; if (rb != null) foreach (var f in rb.Fiefs.Values) if (f.Realm == realm) sum += tax.PayrollSeason(f.Fief);
        int day = Day;
        foreach (var a in Book.Hosts.Values) if (a.Realm == realm) sum += NeedsOf(a, day).PayMites * TimeConst.DaysPerSeason;
        return sum;
    }

    /// <summary>The coin the realm owes in open tallies.</summary>
    public long TalliesOwed(int realm) { long t = 0; foreach (var x in Book.Tallies.Values) if (x.State == TallyState.Owed && x.Realm == realm) t += x.Owing; return t; }

    /// <summary>
    /// The season's step (S5, hour 5 of a season's first day): each tally past due is paid in coin from its realm's treasury while that holds more than a season of its payroll (<c>Purveyance</c>), in tally order; a tally four seasons past
    /// due is defaulted, the grievance added again and <c>purveyance_unpaid</c> written (caused by the taking); tallies a year old and settled are dropped; the season's scutage is written. Returns the coin paid.
    /// </summary>
    public long SeasonStep(int day)
    {
        long paid = 0; var led = _econ.Ledger; var unrest = UnrestBk;
        var payroll = new Dictionary<int, long>();
        foreach (var t in Book.Tallies.Values.ToList())
        {
            if (t.State != TallyState.Owed || day < t.DueDay) continue;
            var from = RealmPurse(t.Realm);
            if (!from.IsNone && led.TryFind(PurseKind.Household, new EntityId(t.Household), out var to))
            {
                if (!payroll.TryGetValue(t.Realm, out long keep)) payroll[t.Realm] = keep = RealmPayrollSeason(t.Realm);
                long pay = Math.Min(t.Owing, led.Balance(from).Mites - keep);
                if (pay > 0 && led.Transfer(from, to, new Coin(pay), TransferReason.Purveyance)) { t.PaidMites += pay; paid += pay; Book.Stats[2] += pay; }
            }
            if (t.Owing == 0) { t.State = TallyState.Paid; t.PaidDay = day; }
        }
        var defaulted = new SortedDictionary<ulong, List<PurveyanceTally>>();
        foreach (var t in Book.Tallies.Values.ToList())
        {
            if (t.State != TallyState.Owed || day < t.DueDay + SupplyConfig.TallyDefaultSeasons * TimeConst.DaysPerSeason) continue;
            t.State = TallyState.Defaulted; t.PaidDay = day; Book.Stats[3] += t.Owing;
            unrest?.Add(t.Household, "purveyance", day, t.Entry);
            if (!defaulted.TryGetValue(t.Entry, out var l)) defaulted[t.Entry] = l = [];
            l.Add(t);
        }
        foreach (var (entry, list) in defaulted)
        {
            int s = list[0].Settlement; var sid = _pop.Region.Settlements[s].Id; long owed = list.Sum(x => x.Owing);
            var cause = entry != 0 ? new Cause(CauseKind.Supply, new EntityId(entry)) : new Cause(CauseKind.Supply, null, NoteKey: "ch_note_war_tally_unpaid");
            Chron.Chron(RuleCtx.At(day), "purveyance_unpaid", "ch_war_tally_default", [ChronParam.Place("place", sid), ChronParam.Count("households", list.Count), ChronParam.Mites("amount", owed)], [sid], [cause]);
        }
        foreach (var id in Book.Tallies.Where(kv => kv.Value.State != TallyState.Owed && day - kv.Value.PaidDay > TimeConst.DaysPerYear).Select(kv => kv.Key).ToList()) Book.Tallies.Remove(id);
        NoteScutage(day);
        return paid;
    }
}
