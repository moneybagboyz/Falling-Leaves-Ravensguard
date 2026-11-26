using Ravensguard.Content;
using Ravensguard.Sim.Combat.Abstract;
using Ravensguard.Sim.Continental;
using Ravensguard.Sim.Core;

namespace Ravensguard.Sim.Armies;

/// <summary>The constants of the count hosts on the continent (abstract-war 4.4; code for v1, as the continent tick's).</summary>
public static class CountRules
{
    /// <summary>Metres a host marches a game day (armies spec 3.7: 3.7 km).</summary>
    public const int MarchMPerDay = 3700;
    /// <summary>Grams of grain a man eats a day (the continent's own figure).</summary>
    public static readonly int GramsPerManDay = ContinentRules.DailyGramsPerPerson[0];
    /// <summary>Days of food a host tops up to from its realm's stores, and the days of the region's own people it leaves in them.</summary>
    public const int SupplyDays = 10, ReserveDays = 10;
    /// <summary>A forager takes at most this share of a region's grain a day (permille) and spoils this share again of what he takes (percent).</summary>
    public const int ForageMaxPm = 20, ForageWastePct = 25;
    /// <summary>A hungry host loses this share of its men a day to desertion (permille), and its morale this many points.</summary>
    public const int DesertPm = 30, HungerMorale = 40;
    /// <summary>A summary town's levy (permille of its people), the morale behind walls, and the host to levy ratio that makes a storm worth the lives (x10), and the days spent before it.</summary>
    public const int LevyPm = 70, WallMoralePm = 1150, StormRatioX10 = 15, StormAfterDays = 4;
    /// <summary>War score: a place taken, and the most a field battle earns.</summary>
    public const int PlaceTakenPoints = 8, BattleMaxPoints = 6;
}

/// <summary>
/// The count hosts on the continent (abstract-war 4.4): they march along the land edges at 3.7 km a day, eat from their realm's summary stores or forage the regions they cross, besiege
/// summary towns (starvation, or a storm fought as a count battle against the town's levy), and fight each other as count battles where they meet. Everything moves between the summaries
/// and the hosts in whole units and is entered in the ledger, so a season's grain, people and coin add up exactly (AW-11); coin is never touched. The day step is deterministic: hosts in id
/// order, rounding draws on stream 0x7E3 (host, day) and 0x7E2 (the battles).
/// </summary>
public sealed class ContinentArmies : IContinentArmies
{
    private readonly ArmyBook _book;
    private readonly CountBattleDef _def;

    public ContinentArmies(ArmyBook book, CountBattleDef? def = null) { _book = book; _def = def ?? new CountBattleDef { Id = "count" }; }

    public CountHostBook Hosts => _book.Count;

    // ------------------------------------------------------------------ raising and disbanding

    /// <summary>A realm raises a host in a summary region of its own: the men leave the region's people, the host carries ten days' grain from its stores. Null if the region cannot spare them.</summary>
    public CountHost? Raise(ContinentState st, int realm, int home, int[] men, int foe, int war, int objective, int day, int[]? qualityPm = null)
    {
        var s = st.Summaries[home];
        int total = men.Sum();
        if (s.Detailed || total <= 0 || total * 2 > s.Pop) return null;
        var h = new CountHost { Id = _book.Count.NextId++, Realm = realm, Home = home, War = war, Foe = foe, Region = home, Objective = objective, FormedDay = day, State = CountState.Standing };
        Array.Copy(men, h.Men, CountScenarios.Classes);
        if (qualityPm != null) Array.Copy(qualityPm, h.QualityPm, CountScenarios.Classes);
        s.Pop -= total;
        TopUp(s, h);
        _book.Count.Hosts[h.Id] = h; _book.Touch();
        return h;
    }

    /// <summary>The host is disbanded where it stands (or at home): its men, its prisoners and its grain go into the region it is in, the men who are somebody's home people back to their own home.</summary>
    public void Disband(ContinentState st, CountHost h)
    {
        var home = st.Summaries[h.Home];
        home.Pop += h.Total + h.Prisoners;
        var at = h.Region >= 0 ? st.Summaries[h.Region] : home;
        at.StockG[0] += h.GrainG;
        Array.Clear(h.Men); h.Prisoners = 0; h.GrainG = 0; h.State = CountState.Disbanded;
        _book.Count.Hosts.Remove(h.Id); _book.Touch();
    }

    // ------------------------------------------------------------------ the season and the day

    /// <summary>The continent tick's hook: a season's days of the hosts.</summary>
    public void Season(ContinentState st, GameTime now)
    {
        if (_book.Count.Hosts.Count == 0) return;
        long d0 = now.TotalDays;
        for (int d = 0; d < TimeConst.DaysPerSeason; d++) Day(st, (int)(d0 + d));
    }

    public void Day(ContinentState st, int day)
    {
        var book = _book.Count;
        if (book.Hosts.Count == 0) return;
        ulong seed = st.Seed;
        foreach (var h in book.Hosts.Values.ToList())
        {
            if (!book.Hosts.ContainsKey(h.Id) || h.State is CountState.InDetail or CountState.Disbanded) continue;
            Eat(st, h, day, seed);
            if (h.Total == 0) { Disband(st, h); continue; }
            Move(st, h);
        }
        // hosts of foes who stand in the same region fight (ids ascending)
        var live = book.Hosts.Values.Where(x => x.Region >= 0 && x.State is CountState.Standing or CountState.Besieging).ToList();
        for (int i = 0; i < live.Count; i++)
            for (int j = i + 1; j < live.Count; j++)
            {
                var a = live[i]; var b = live[j];
                if (a.Region != b.Region || a.Total == 0 || b.Total == 0 || a.Realm == b.Realm || !(a.Foe == b.Realm || b.Foe == a.Realm)) continue;
                Meet(st, a, b, day, seed);
            }
        foreach (var h in book.Hosts.Values.ToList())
        {
            if (h.State == CountState.Besieging) Siege(st, h, day, seed);
            if (book.Hosts.ContainsKey(h.Id) && h.Total == 0) Disband(st, h);
            else if (book.Hosts.ContainsKey(h.Id) && h.Objective == h.Home && h.Region == h.Home && h.State == CountState.Standing && h.Foe == 255) Disband(st, h);
        }
    }

    // ------------------------------------------------------------------ eating and foraging

    private static void TopUp(RegionSummary s, CountHost h)
    {
        long want = (long)(h.Total + h.Prisoners) * CountRules.GramsPerManDay * CountRules.SupplyDays - h.GrainG;
        if (want <= 0) return;
        long reserve = (long)s.Pop * CountRules.GramsPerManDay * CountRules.ReserveDays;
        long take = Math.Min(want, Math.Max(0, s.StockG[0] - reserve));
        s.StockG[0] -= take; h.GrainG += take;
    }

    private void Eat(ContinentState st, CountHost h, int day, ulong seed)
    {
        var led = _book.Count.Ledger;
        long need = (long)(h.Total + h.Prisoners) * CountRules.GramsPerManDay;
        if (h.Region >= 0 && !st.Summaries[h.Region].Detailed)
        {
            var s = st.Summaries[h.Region];
            if (s.Realm == h.Realm) TopUp(s, h);
            else if (h.GrainG < need)
            {
                int j = DetRng.Range(seed, day, (ulong)h.Id, RngStreams.CountHostRounding, 0, 900, 1101);
                long take = Math.Min(Math.Min(need * 3 - h.GrainG, s.StockG[0] * CountRules.ForageMaxPm / 1000) * j / 1000, s.StockG[0]);
                take = Math.Max(0, take);
                long waste = Math.Min(s.StockG[0] - take, take * CountRules.ForageWastePct / 100);
                s.StockG[0] -= take + waste; h.GrainG += take;
                led[CountLedger.GrainForaged] += take; led[CountLedger.GrainWasted] += waste;
            }
        }
        long eaten = Math.Min(h.GrainG, need);
        h.GrainG -= eaten; led[CountLedger.GrainEaten] += eaten;
        if (eaten < need)
        {
            h.HungryDays++; h.MoralePm = Math.Max(300, h.MoralePm - CountRules.HungerMorale);
            int lose = Math.Min(h.Total, Math.Max(1, h.Total * CountRules.DesertPm / 1000));
            Remove(h, lose, out _);
            st.Summaries[h.Home].Pop += lose; led[CountLedger.MenDeserted] += lose;
        }
        else { h.HungryDays = 0; h.MoralePm = Math.Min(1000, h.MoralePm + 10); }
    }

    /// <summary>Takes <paramref name="n"/> men off the host, class by class in proportion (the largest remainders first), and says how many of each.</summary>
    private static void Remove(CountHost h, int n, out int[] by)
    {
        by = new int[CountScenarios.Classes];
        int total = h.Total; if (total == 0 || n <= 0) return;
        n = Math.Min(n, total);
        int left = n;
        var rem = new long[CountScenarios.Classes];
        for (int c = 0; c < CountScenarios.Classes; c++) { long m = (long)h.Men[c] * n; by[c] = (int)(m / total); rem[c] = m % total; left -= by[c]; }
        while (left > 0)
        {
            int best = -1;
            for (int c = 0; c < CountScenarios.Classes; c++) if (by[c] < h.Men[c] && (best < 0 || rem[c] > rem[best])) best = c;
            if (best < 0) break;
            by[best]++; rem[best] = -1; left--;
        }
        for (int c = 0; c < CountScenarios.Classes; c++) h.Men[c] -= by[c];
    }

    // ------------------------------------------------------------------ the march

    private static int[] DistancesFrom(ContinentState st, int target)
    {
        var dist = new int[st.Summaries.Length]; Array.Fill(dist, int.MaxValue);
        var q = new Queue<int>(); dist[target] = 0; q.Enqueue(target);
        while (q.Count > 0)
        {
            int r = q.Dequeue();
            foreach (int ei in st.EdgesOf[r]) { int o = st.Edges[ei].Other(r); if (dist[o] == int.MaxValue) { dist[o] = dist[r] + 1; q.Enqueue(o); } }
        }
        return dist;
    }

    private void Move(ContinentState st, CountHost h)
    {
        if (h.OnEdge)
        {
            h.WalkedM += CountRules.MarchMPerDay;
            var e = st.Edges[h.Edge];
            if (h.WalkedM < e.LengthKm * 1000) return;
            h.Region = e.Other(h.FromRegion); h.Edge = -1; h.FromRegion = -1; h.WalkedM = 0; h.State = CountState.Standing;
            Arrive(st, h);
            return;
        }
        if (h.Region < 0 || h.Objective < 0 || h.Region == h.Objective || h.State == CountState.Besieging) return;
        var dist = DistancesFrom(st, h.Objective);
        if (dist[h.Region] == int.MaxValue) return;
        int bestEdge = -1, bestRegion = -1;
        foreach (int ei in st.EdgesOf[h.Region])
        {
            int o = st.Edges[ei].Other(h.Region);
            if (dist[o] < dist[h.Region] && (bestEdge < 0 || o < bestRegion)) { bestEdge = ei; bestRegion = o; }
        }
        if (bestEdge < 0) return;
        h.Edge = bestEdge; h.FromRegion = h.Region; h.Region = -1; h.WalkedM = 0; h.State = CountState.Marching;
    }

    private void Arrive(ContinentState st, CountHost h)
    {
        var s = st.Summaries[h.Region];
        if (h.Region != h.Objective || s.Detailed) return;
        if (s.Realm != h.Realm && h.Foe == s.Realm) { h.State = CountState.Besieging; h.SiegeDays = 0; }
    }

    // ------------------------------------------------------------------ battles and sieges

    private void Meet(ContinentState st, CountHost a, CountHost b, int day, ulong seed)
    {
        var res = CountBattle.Fight(_def, seed, ((ulong)(uint)day << 24) | ((ulong)(uint)a.Id << 12) | (uint)b.Id, [a.Men, b.Men], [a.QualityPm, b.QualityPm], [a.MoralePm, b.MoralePm]);
        var led = _book.Count.Ledger; led[CountLedger.Battles]++;
        var hs = new[] { a, b };
        int[] lost = new int[2];
        for (int s = 0; s < 2; s++)
        {
            var h = hs[s];
            int dead = res.DeadOf(s), crip = res.CrippledOf(s), rec = res.Total(res.Recovering, s), fled = res.Total(res.Fled, s), capt = res.Total(res.Captured, s);
            for (int c = 0; c < CountScenarios.Classes; c++) h.Men[c] = res.Standing[s][c];
            led[CountLedger.MenDead] += dead; led[CountLedger.MenCrippled] += crip;
            st.Summaries[h.Home].Pop += crip + rec + fled;                       // the hurt and the runaways go home
            led[CountLedger.MenFled] += fled;
            lost[s] = dead + crip + rec;
            hs[1 - s].Prisoners += capt; led[CountLedger.MenCaptured] += capt;
            h.MoralePm = Math.Max(300, h.MoralePm - 100 * lost[s] / Math.Max(1, res.Total(res.Start, s)) * 5);
        }
        int w = res.Winner;
        if (w >= 0)
        {
            var loser = hs[1 - w];
            if (loser.Total > 0) Retreat(loser);
            ScoreEvent(hs[w], WarEventKind.FieldBattle, Math.Min(CountRules.BattleMaxPoints, lost[1 - w] * 100 / Math.Max(1, res.Total(res.Start, 1 - w)) / 2), lost[1 - w], lost[w], a.Region, day);
            if (loser.Total == 0) ScoreEvent(hs[w], WarEventKind.HostDestroyed, CountRules.BattleMaxPoints, lost[1 - w], 0, a.Region, day);
        }
    }

    private static void Retreat(CountHost h) { h.Objective = h.Home; h.Foe = 255; h.State = CountState.Standing; h.SiegeDays = 0; }

    private void Siege(ContinentState st, CountHost h, int day, ulong seed)
    {
        var led = _book.Count.Ledger;
        var r = st.Summaries[h.Region];
        if (r.Realm == h.Realm || r.Detailed) { h.State = CountState.Standing; h.Objective = -1; return; }
        h.SiegeDays++;
        long need = (long)r.Pop * CountRules.GramsPerManDay;
        long eat = Math.Min(r.StockG[0], need);
        r.StockG[0] -= eat; led[CountLedger.SiegeEaten] += eat;
        if (r.StockG[0] <= 0) { Fall(st, h, r, day); return; }
        int levy = (int)((long)r.Pop * CountRules.LevyPm / 1000);
        if (h.SiegeDays >= CountRules.StormAfterDays && (long)h.Total * 10 >= (long)levy * CountRules.StormRatioX10) Storm(st, h, r, levy, day, seed);
    }

    private void Storm(ContinentState st, CountHost h, RegionSummary r, int levy, int day, ulong seed)
    {
        var led = _book.Count.Ledger; led[CountLedger.Storms]++;
        var def = new int[CountScenarios.Classes]; def[(int)CountClass.Levy] = Math.Max(1, levy);
        var res = CountBattle.Fight(_def, seed, ((ulong)(uint)day << 24) | ((ulong)(uint)h.Id << 12) | 0xFFF, [h.Men, def], [h.QualityPm, [1000, 1000, 1000, 1000, 1000]], [h.MoralePm, CountRules.WallMoralePm]);
        int dead = res.DeadOf(0), crip = res.CrippledOf(0), rec = res.Total(res.Recovering, 0), fled = res.Total(res.Fled, 0), capt = res.Total(res.Captured, 0);
        for (int c = 0; c < CountScenarios.Classes; c++) h.Men[c] = res.Standing[0][c];
        led[CountLedger.MenDead] += dead; led[CountLedger.MenCrippled] += crip; led[CountLedger.MenFled] += fled;
        st.Summaries[h.Home].Pop += crip + rec + fled + capt;                  // the hurt, the runaways and the men the town let go go home
        led[CountLedger.MenCaptured] += capt;
        int townDead = res.DeadOf(1);
        r.Pop = Math.Max(0, r.Pop - townDead); led[CountLedger.MenDead] += townDead;      // the town's dead are men of its households; its hurt stay in it
        led[CountLedger.Battles]++;
        int own = dead + crip + rec, theirs = townDead + res.CrippledOf(1) + res.Total(res.Recovering, 1);
        ScoreEvent(h, WarEventKind.FieldBattle, Math.Min(CountRules.BattleMaxPoints, theirs * 100 / Math.Max(1, levy) / 2), theirs, own, h.Region, day);
        if (res.Winner == 0 && h.Total > 0) Fall(st, h, r, day);
        else { Retreat(h); h.Foe = r.Realm; }
    }

    private void Fall(ContinentState st, CountHost h, RegionSummary r, int day)
    {
        r.Realm = (byte)h.Realm;
        _book.Count.Ledger[CountLedger.PlacesTaken]++;
        ScoreEvent(h, WarEventKind.PlaceTaken, CountRules.PlaceTakenPoints, 0, 0, h.Region, day);
        h.State = CountState.Standing; h.Objective = -1; h.SiegeDays = 0;
    }

    private void ScoreEvent(CountHost doer, WarEventKind kind, int points, int lost, int ownLost, int place, int day)
    {
        if (doer.War <= 0) return;
        _book.WarEvents.Add(new WarEvent { War = doer.War, Day = day, Kind = kind, Doer = doer.Realm, Points = points, Lost = lost, OwnLost = ownLost, Place = place });
        _book.Touch();
    }

    // ------------------------------------------------------------------ the books

    /// <summary>What the conservation tests add up: the people in the summaries and the hosts, the grain in the summaries and the hosts' wagons (grams), with what has gone out of the world.</summary>
    public (long People, long GrainG) Totals(ContinentState st)
    {
        long people = 0, grain = 0;
        foreach (var s in st.Summaries) { people += s.Pop; grain += s.StockG[0]; }
        foreach (var h in _book.Count.Hosts.Values) { people += h.Total + h.Prisoners; grain += h.GrainG; }
        var l = _book.Count.Ledger;
        return (people + l[CountLedger.MenDead], grain + l[CountLedger.GrainEaten] + l[CountLedger.GrainWasted] + l[CountLedger.SiegeEaten]);
    }
}
