using Ravensguard.Sim.Abstract;
using Ravensguard.Sim.WarSupply;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.People;
using Ravensguard.Sim.People.Vital;

namespace Ravensguard.Sim.Armies;

/// <summary>The numbers of war flight and return (war's toll 3.4): permille chances by the worst thing a settlement saw in the season, the poorest third's doubling and the return rolls.</summary>
public static class FlightConfig
{
    public const int ForagedPm = 50, RaidedPm = 150, BesiegedPm = 300, SackedPm = 600, PoorCapPm = 900;
    public const int ReturnPm = 500, ReturnNoHousePm = 100, CalmSeasons = 2, KeepSeasons = 60;
    public const int HostM = 8000, SafeM = 10_000;
    public static int ChancePm(int level) => level switch { 1 => ForagedPm, 2 => RaidedPm, 3 => BesiegedPm, >= 4 => SackedPm, _ => 0 };
}

/// <summary>
/// P8-WT-1, war flight and return (war's toll 3.4). What war does to a settlement is noted as it happens (<see cref="NoteTouch"/>: foraged 1, raided 2, besieged 3, sacked 4, the worst of the season kept); on the season's first
/// day each household of a touched settlement rolls stream 0x880 (keyed on its id and the day) against the chance of the worst event, doubled (cap 900) for the poorest third by coin, and the household that rolls under goes whole,
/// with its purse and store, to the nearest settlement of its realm beyond 10 km that no host is within 8 km of and where a dwelling stands free, through <see cref="Demography.Relocate"/> (counted as a departure and an arrival). After peace,
/// or after two seasons with no host within 8 km of its home, each fled household rolls stream 0x881 at 500 per mille a season (100 when its house is taken) and comes home. Both are written as one chronicle entry a settlement, the return caused by the flight.
/// </summary>
public sealed partial class ArmyWorld
{
    /// <summary>The soak's observers, not saved: a household fled (from, to, persons) or came home (to its home, persons).</summary>
    public Action<int, int, int>? OnFlight { get; set; }
    public Action<int, int, int>? OnReturn { get; set; }

    /// <summary>War touched a settlement today: the worst level of the season is kept with the chronicle entry that did it.</summary>
    public void NoteTouch(int settlement, int level, int day, ulong entry)
    {
        if ((uint)settlement >= (uint)Region.Settlements.Count || level <= 0) return;
        if (Book.Touched.TryGetValue(settlement, out var r) && r[1] >= level) return;
        Book.Touched[settlement] = [settlement, level, day, (long)entry];
        Book.Touch();
    }

    private bool HostNear(List<(long X, long Y)> hosts, int settlement)
    {
        var (cx, cy) = CenterMm(settlement); long r = FlightConfig.HostM * 1000L;
        foreach (var (x, y) in hosts) { long dx = cx - x, dy = cy - y; if (dx * dx + dy * dy <= r * r) return true; }
        return false;
    }

    /// <summary>The season's first day (hour 3): the fled who may come home do, then the touched settlements' households roll for flight.</summary>
    public void FlightSeason(ArmyEnv e)
    {
        var hs = Supply; if (hs == null || (Book.Touched.Count == 0 && Book.Fled.Count == 0)) return;
        var ob = e.Ob ?? new RegionOutbox(W.Ids);
        long sec = e.Hour * 3600; var hosts = new List<(long X, long Y)>();
        foreach (var h in Book.Hosts.Values.OrderBy(h => h.Id))
            if (h.State is not (HostState.Summoned or HostState.Mustering or HostState.Disbanding) && h.Supply != 0) hosts.Add(PositionOfHost(h, sec));
        ReturnHome(e, hs, ob, hosts);
        Flee(e, hs, ob, hosts);
        Book.Touched.Clear(); Book.Touch();
    }

    private void Flee(ArmyEnv e, HostSupply hs, RegionOutbox ob, List<(long X, long Y)> hosts)
    {
        var ht = Households; var pt = People; var econ = W.Economy; int season = e.SeasonNo; int day = (int)e.Day;
        foreach (var (s, row) in Book.Touched.ToList())
        {
            int level = (int)row[1]; int chance = FlightConfig.ChancePm(level); if (chance <= 0) continue;
            int realm = hs.RealmOf(s);
            // the households of the settlement, the poorest third by coin (ties by id) doubled
            var hhs = new List<int>();
            for (int h = 0; h < ht.Count; h++) if (ht.Settlement[h] == s && ht.MemberCount[h] > 0 && ht.Class[h] != SocialClass.Gentry) hhs.Add(h);
            if (hhs.Count == 0) continue;
            var poor = new HashSet<int>(); var byCoin = hhs.OrderBy(h => econ.Ledger.Balance(econ.HouseholdPurse[h]).Mites).ThenBy(h => h).Take(hhs.Count / 3);
            foreach (int h in byCoin) poor.Add(h);
            List<int>? dests = null; int went = 0; var pending = new List<long[]>();
            foreach (int hh in hhs)
            {
                int p = poor.Contains(hh) ? Math.Min(FlightConfig.PoorCapPm, chance * 2) : chance;
                if (DetRng.Range(W.Seed, day, ht.IdOf(hh).Value, RngStreams.WarFlightChoice, 0, 0, 1000) >= p) continue;
                if (HeroIn(hh)) continue;
                dests ??= SafeSettlements(hs, s, realm, hosts);
                int members = ht.MemberCount[hh], to = -1, lot = -1;
                foreach (int t in dests) { var (fs, fl) = Demography.FreeLotIn(W, t, members); if (fs >= 0) { to = fs; lot = fl; break; } }
                if (to < 0 && dests.Count > 0) { to = dests[0]; lot = -1; }                      // none stands free: they camp in the nearest safe settlement
                if (to < 0) continue;
                int oldS = ht.Settlement[hh], oldLot = ht.Lot[hh]; ulong hid = ht.IdOf(hh).Value;
                if (!Demography.Relocate(W, hh, to, lot, e.Time, ob)) continue;
                went += members; OnFlight?.Invoke(oldS, to, members);
                // the origin is the first home it left (a household that flees again still means to go home)
                int from = oldS, homeLot = oldLot; long calm = 0;
                for (int k = Book.Fled.Count - 1; k >= 0; k--) if ((ulong)Book.Fled[k][0] == hid) { from = (int)Book.Fled[k][1]; homeLot = (int)Book.Fled[k][6]; calm = Book.Fled[k][5]; Book.Fled.RemoveAt(k); }
                Book.Fled.Add([(long)hid, from, to, season, 0, calm, homeLot]);
                pending.Add(Book.Fled[^1]);
            }
            if (went == 0) continue;
            Cause[] cause = row[3] != 0 ? [new Cause(CauseKind.WarFlight, new EntityId((ulong)row[3]))] : [];
            var entry = e.Chronicler.Chron(e.Time, "war_flight", "ch_p8_war_flight", [ChronParam.Place("place", PlaceId(s)), ChronParam.Count("count", went)], [PlaceId(s)], cause);
            foreach (var r in pending) r[4] = (long)entry.Value;
        }
    }

    private bool HeroIn(int hh) { foreach (int p in Households.Members(hh)) if (IsHero(People.IdOf(p).Value)) return true; return false; }

    /// <summary>The settlements a household of <paramref name="s"/> may run to, nearest first (ties by index): its realm's, beyond 10 km, with no host within 8 km and not touched this season.</summary>
    private List<int> SafeSettlements(HostSupply hs, int s, int realm, List<(long X, long Y)> hosts)
    {
        var list = new List<(int D, int T)>();
        for (int t = 0; t < Region.Settlements.Count; t++)
        {
            if (t == s || hs.RealmOf(t) != realm || Book.Touched.ContainsKey(t)) continue;
            int d = DistM(s, t); if (d <= FlightConfig.SafeM || HostNear(hosts, t)) continue;
            list.Add((d, t));
        }
        list.Sort((a, b) => a.D != b.D ? a.D.CompareTo(b.D) : a.T.CompareTo(b.T));
        return [.. list.Select(x => x.T)];
    }

    private void ReturnHome(ArmyEnv e, HostSupply hs, RegionOutbox ob, List<(long X, long Y)> hosts)
    {
        if (Book.Fled.Count == 0) return;
        var ht = Households; int season = e.SeasonNo; int day = (int)e.Day; var pb = Politics;
        var back = new SortedDictionary<int, (int Count, ulong Cause)>();
        for (int i = 0; i < Book.Fled.Count; i++)
        {
            var r = Book.Fled[i]; int hh = ht.IndexOf(new EntityId((ulong)r[0]));
            int from = (int)r[1], to = (int)r[2];
            if (hh < 0 || ht.MemberCount[hh] == 0 || ht.Settlement[hh] != to || season - r[3] > FlightConfig.KeepSeasons) { Book.Fled.RemoveAt(i--); continue; }
            r[5] = HostNear(hosts, from) ? 0 : r[5] + 1;
            if (r[3] == season) continue;
            int realm = hs.RealmOf(from);
            bool peace = pb == null || realm < 0 || pb.WarOf(realm) == null;
            if (!peace && r[5] < FlightConfig.CalmSeasons) continue;
            int members = ht.MemberCount[hh], lot = (int)r[6];
            var pop = W.Population; bool stands = lot >= 0 && (uint)lot < (uint)pop.LotHousehold[from].Length && pop.LotHousehold[from][lot] < 0;
            if (!stands) { var (_, fl) = Demography.FreeLotIn(W, from, members); lot = fl; }                  // the house is taken: another dwelling, or a camp at home
            int pm = stands ? FlightConfig.ReturnPm : FlightConfig.ReturnNoHousePm;
            if (DetRng.Range(W.Seed, day, (ulong)r[0], RngStreams.WarReturn, 0, 0, 1000) >= pm) continue;
            if (!Demography.Relocate(W, hh, from, lot, e.Time, ob)) continue;
            OnReturn?.Invoke(to, from, members);
            back.TryGetValue(from, out var b); back[from] = (b.Count + members, b.Cause != 0 ? b.Cause : (ulong)r[4]);
            Book.Fled.RemoveAt(i--);
        }
        foreach (var (s, (count, cause)) in back)
            e.Chronicler.Chron(e.Time, "war_return", "ch_p8_war_return", [ChronParam.Place("place", PlaceId(s)), ChronParam.Count("count", count)], [PlaceId(s)],
                cause != 0 ? [new Cause(CauseKind.WarReturn, new EntityId(cause))] : []);
        if (back.Count > 0) Book.Touch();
    }
}
